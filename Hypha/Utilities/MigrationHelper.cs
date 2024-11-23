using Alta.Chunks;
using Alta.Map;
using Alta.Networking;
using Alta.Networking.Servers;
using Alta.Utilities;
using HarmonyLib;
using Hypha.Migration;
using MelonLoader;
using System;
using System.CodeDom;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Profiling;

namespace Hypha.Utilities
{
    [HarmonyPatch(typeof(NetworkScene), nameof(NetworkScene.InitializeAsServer))]
    public static class NetworkManagerFiller
    {
        [HarmonyPrefix]
        public static async void FillInit(NetworkScene __instance)
        {
            if ((ModServerHandler.Current.ServerInfo.Target & 2) != 0) __instance.IsSimpleServer = true;
            __instance.Spawner = new ServerSpawnManager(__instance, __instance.entityManager);

            if (AstarPath.active == null && __instance.aStar != null) GameObject.Instantiate(__instance.aStar);

            if (__instance.GlobalChunk == null) __instance.globalChunk = __instance.transform.Find("Global Chunk").GetComponent<Chunk>();
            if (__instance.VoidChunk == null) __instance.voidChunk = __instance.transform.Find("Void Chunk").GetComponent<Chunk>();

            __instance.GlobalChunk.InitializeContentManager();
            __instance.entityManager.RegisterEmbedded(__instance.embeddedEntities);
            await __instance.GlobalChunk.ForceLoad();

            if (__instance.VoidChunk != null)
            {
                __instance.VoidChunk.InitializeContentManager();
                await __instance.VoidChunk.ForceLoad();
            }

            __instance.entityManager.InitializeAsServer(__instance.embeddedEntities);
            __instance.entityManager.ChunkEmbedded(__instance.embeddedEntities);

            GarbageCollectTimer garbageTimer = __instance.GetComponent<GarbageCollectTimer>();
            if (garbageTimer != null) garbageTimer.enabled = true;
            NetworkScene.sceneLogger.Info("TEMP pre server started");
            // if (__instance.ServerStarted != null) Irrelevant as nothing uses it. You should probably look into using a different kind of ServerStarted event
            NetworkScene.sceneLogger.Info("TEMP scene started");
        }
    }

    [HarmonyPatch(typeof(StreamerManager), nameof(StreamerManager.Update))]
    public static class StreamerManagerReplacer
    {
        public static bool Prefix(StreamerManager __instance)
        {
            __instance.gameObject.AddComponent<ModStreamerManager>();
            UnityEngine.Object.Destroy(__instance);

            Hypha.Logger.Msg(ConsoleColor.Blue, "Successfully swapped out StreamerManager for ModStreamerManager :)");
            return false;
        }
    }

    [HarmonyPatch(typeof(ChunkFileHelper), nameof(ChunkFileHelper.GetFolder))]
    public static class ChunkFolderFix
    {
        public static void Postfix(ref IAltaFolder __result, int playerIdentifier = 0)
        {
            IAltaFolder altaFolder;

            if (playerIdentifier == 0) altaFolder = ModServerHandler.Current.SaveUtility.ChunksFolder;
            else altaFolder = ModServerHandler.Current.SaveUtility.PlayerSaveUtility.PlayerFolder.GetSubfolder(playerIdentifier.ToString());

            __result = altaFolder;
        }
    }

    [HarmonyPatch(typeof(ServerHandler), nameof(ServerHandler.StartDummyLocalServer))]
    public static class AntiNewServerHandler
    {
        public static bool Prefix() => false;
    }

    [HarmonyPatch(typeof(PlayerDataFileHelper<PlayerSave>), "GetAsync")]
    public static class GetAsyncFixer
    {
        private static Dictionary<Type, object> dataFolders = new Dictionary<Type, object>();

        public static bool Prefix(object __instance, ref Task<IAltaFile> __result, string name)
        {
            Type T = __instance.GetType().GetGenericArguments()[0];
            IAltaFolder result = null;

            if (dataFolders.TryGetValue(T, out object dataHelper)) result = dataHelper as IAltaFolder;
            else
            {
                Hypha.Logger.Msg(ConsoleColor.Yellow, "No matching generic type was found in the dataFolders list. Adding one");
                IAltaFolder newFolder = ModServerHandler.Current.SaveUtility.SaveFolder.GetSubfolder(T.Name);
                dataFolders.Add(T, newFolder);

                result = newFolder;

                __result = GetAsyncNew(name, result, T);
            }

            return false;
        }

        private static async Task<IAltaFile> GetAsyncNew(string name, IAltaFolder result, Type T)
        {
            AltaFile file = result.GetFile(name) as AltaFile;
            await AltaFileExtension.ReadAsync(file, T);
            
            if (file.content == null) file.content = (IAltaFileFormat)Activator.CreateInstance(T);

            return file;
        }
    }

    public static class AltaFileExtension
    {
        internal static async Task<object> ReadAsync(this AltaFile file, Type fileFormat)
        {
            file.isUnloading = false;
            object t;
            if (file.IsReading)
            {
                await file.readingTask;
                t = file.content;
            }
            else if (file.content != null && file.content.GetType() == fileFormat) t = file.content;
            
            else
            {
                if (file.content != null) AltaFile.logger.Error("Invalid format of content. Having to rehandle.");

                file.readingTask = Task.Run(delegate
                {
                    ReadingTask(file, fileFormat);
                });

                Task cached = file.readingTask;
                await file.readingTask;

                if (file.readingTask == cached) file.readingTask = null;
                t = file.content;
            }
            return t;
        }

        private static void ReadingTask(this AltaFile file, Type type)
        {
            bool flag = false;
            try
            {
                file.fileInfo.Refresh();
                if (!file.fileInfo.Exists)
                {
                    file.content = null;
                }
                else
                {
                    flag = true;
                    IAltaFileFormat t = Activator.CreateInstance(type) as IAltaFileFormat;
                    using (FileStream fileStream = file.fileInfo.OpenRead())
                    {
                        t.ReadFrom(file.fileInfo, fileStream);
                        file.content = t;
                    }
                }
            }
            catch (Exception ex)
            {
                AltaFile.logger.Error(ex, "Error while reading. FileName: {0}", new object[] { file.FullName });
            }
            finally
            {
                if (flag) Profiler.EndThreadProfiling();
            }
        }
    }
}
