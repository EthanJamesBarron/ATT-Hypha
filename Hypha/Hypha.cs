using Alta.Api.Client.Clients.High_Level;
using Alta.Api.DataTransferModels.Models.Common;
using Alta.Api.DataTransferModels.Models.Responses;
using Alta.Api.DataTransferModels.Models.Shared;
using Alta.Character;
using Alta.Chunks;
using Alta.Map;
using Alta.Networking;
using Alta.Utilities;
using CrossGameplayApi;
using HarmonyLib;
using MelonLoader;
using Mono.Cecil;
using MonoMod.Cil;
using MonoMod.RuntimeDetour;
using MonoMod.Utils;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.SceneManagement;

[assembly: MelonInfo(typeof(Hypha.Hypha), "Hypha", "0.0.1", "Hypha Team", null)]

namespace Hypha
{
    public class Hypha : MelonPlugin
    {
        public bool IsServerInstance { get; private set; }
        internal static MelonLogger.Instance Logger { get; private set; }
        public static DevGameServerInfo devServerInfo;

        public static event Action OnPrefabWarmup;


        public override async void OnApplicationStarted()
        {
            Logger ??= LoggerInstance;

            foreach (string parameter in Environment.GetCommandLineArgs())
            {
                if (parameter == "$ServerMode")
                {
                    IsServerInstance = true;
                    break;
                }
            }

            devServerInfo = DevGameServerInfo.GetDevServer(IPAddress.Loopback.ToString(), 1757, 0);
        }


        public override void OnLateInitializeMelon()
        {
            if (!CommandLineArguments.Contains("-AlreadyStarted"))
            {
                LaunchNewClientInstance();
                Application.Quit();
            }

            if (IsServerInstance)
            {
                AltaSceneManager.LoadMenuSceneAsync();
            }

            SceneManager.sceneLoaded += (scene, loadMode) =>
            {
                if (scene.name == "Main Menu")
                {
                    if (IsServerInstance)
                    {
                        IServerAccess access = new ServerAccess(devServerInfo, (HighLevelApiClient)ApiAccess.ApiClient);
                        GameModeManager.StartServer(access, false, true, 1757, false);
                        return;
                    }
                }
            };
        }

        public override void OnGUI()
        {
            if (GUILayout.Button("Start initial server"))
            {
                IServerAccess access = new ServerAccess(devServerInfo, (HighLevelApiClient)ApiAccess.ApiClient);
                LaunchNewServerInstance(access, true, 1757);
            }

            if (GUILayout.Button("Join modded server"))
            {
                VrMainMenu.Instance.JoinServer(devServerInfo);
            }
        }


        public static async Task<bool> StartModdedServer(IServerAccess access, bool headless = true, bool externalLaunch = true, int port = 1757, bool runningLocally = true)
        {
            return await GameModeManager.StartServer(new ServerAccess(devServerInfo, (HighLevelApiClient)ApiAccess.ApiClient), false, true, 1757, true);
        }


        public static GameVersion LatestVersion()
        {
            GenericVersionParts genericVersionParts = VersionHelper.Parse(BuildVersion.CurrentVersion.ToString());
            return GameVersion.Parse(genericVersionParts.ToString());
        }


        internal void LaunchNewServerInstance(IServerAccess access, bool headless = false, int port = 1757)
        {
            ServerSaveUtility serverSaveUtility = new(access);
            string logPath = Path.Combine(path2: $"{DateTime.Now:yyyy-MM-dd-HH-mm-ss}" + "_headlessServer.txt", path1: serverSaveUtility.LogsPath);


            string cla = CommandLineArguments.RawCommandLine + " $ServerMode " + " /start_server " + access.ServerInfo.Identifier.ToString() + (headless ? " true " : " false") + port + " /console " + " -logFile \"" + logPath + "\"" + " -AlreadyStarted";
            Process.Start(Environment.GetCommandLineArgs()[0], cla);
        }

        internal void LaunchNewClientInstance()
        {
            string cla = CommandLineArguments.RawCommandLine;
            Process.Start(Environment.GetCommandLineArgs()[0], cla + " -AlreadyStarted");
        }

        internal static void InvokePrefabWarmup() => OnPrefabWarmup?.Invoke();
    }
}
