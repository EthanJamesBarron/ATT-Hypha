using Alta.Api.DataTransferModels.Models.Requests;
using Alta.Api.DataTransferModels.Models.Responses;
using Alta.Map;
using Alta.Meta.UI;
using Alta.Networking;
using Alta.Networking.Scripts.Player;
using Alta.Networking.Servers;
using Alta.Utilities;
using HarmonyLib;
using Hypha.Core;
using Hypha.Migration;
using NLog;
using NLog.Internal;
using Steamworks;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading.Tasks;

namespace Hypha.Utilities
{
    // Fill in "claAppends" to add any extra parameters to the game
    [HarmonyPatch(typeof(Environment), nameof(Environment.GetCommandLineArgs))]
    public static class CLAExtender
    {
        internal static string[] claAppends = new string[0];

        public static void Postfix(ref string[] __result)
        {
            string[] newCLA = new string[__result.Length + claAppends.Length];

            for (int i = 0; i < __result.Length; i++) newCLA[i] = __result[i];
            for (int i = __result.Length; i < newCLA.Length; i++) newCLA[i] = claAppends[i - __result.Length];

            __result = newCLA;
        }
    }


    // Makes the game's usage of NLog visible in the MelonLoader console
    [HarmonyPatch(typeof(Logger))]
    public static class NLogPatches
    {
        [HarmonyPatch(nameof(Logger.WriteToTargets), new Type[] { typeof(LogEventInfo), typeof(TargetWithFilterChain) })]
        public static void Prefix(LogEventInfo logEvent, TargetWithFilterChain targetsForLevel)
        {
            LogAppropriately(logEvent);
        }

        [HarmonyPatch(nameof(Logger.WriteToTargets), new Type[] { typeof(Type), typeof(LogEventInfo), typeof(TargetWithFilterChain) })]
        public static void Prefix(Type wrapperType, LogEventInfo logEvent, TargetWithFilterChain targetsForLevel)
        {
            LogAppropriately(logEvent);
        }

        public static void LogAppropriately(LogEventInfo logEvent)
        {
            string msg = logEvent.CallerMemberName + " ";
            msg += logEvent.FormattedMessage + "Exception next: ";
            msg += logEvent.Exception;

            if (logEvent.Level == LogLevel.Info)
            {
                Hypha.Logger.Msg("NLOG INFO: " + msg);
            }

            else if (logEvent.Level == LogLevel.Error)
            {
                Hypha.Logger.Error("NLOG ERROR: " + msg);
            }

            else if (logEvent.Level == LogLevel.Warn)
            {
                Hypha.Logger.Warning("NLOG WARN: " + msg);
            }

            else if (logEvent.Level == LogLevel.Debug)
            {
                Hypha.Logger.Msg("NLOG DEBUG: " + msg);
            }

            else if (logEvent.Level == LogLevel.Fatal)
            {
                Hypha.Logger.Error("NLOG FATAL: " + msg);
            }

            else if (logEvent.Level == LogLevel.Trace)
            {
                Hypha.Logger.Warning("NLOG TRACE: " + msg);
            }
        }
    }


    [HarmonyPatch(typeof(GameServerInfoExtensions), nameof(GameServerInfoExtensions.JoinServerAsync))]
    public static class JoinServerFix
    {
        public static bool Prefix(GameServerInfo gameServer, ref Task<ServerJoinResult> __result)
        {
            if (gameServer is ModdedServerInfo)
            {
                GameServerInfoExtensions.logger.Debug("Joining modded server, skipping API check");

                __result = JoinResultForModdedServer(gameServer as ModdedServerInfo);

                return false;
            }

            return true;
        }

        internal static async Task<ServerJoinResult> JoinResultForModdedServer(ModdedServerInfo gameServer)
        {
            ServerJoinResult newResult = new()
            {
                IsAllowed = true,
                ConnectionInfo = new()
                {
                    Address = IPAddress.Parse(gameServer.IP),
                    GamePort = gameServer.Port
                }
            };

            return newResult;
        }
    }

    [HarmonyPatch(typeof(PrefabManager), nameof(PrefabManager.PrepareSpawnSetups))]
    public static class PrefabWarmupEvent
    {
        public static void Postfix()
        {
            Hypha.InvokePrefabWarmup();
        }
    }

    [HarmonyPatch(typeof(PerPlayerContent<PlayerSave>), "LoadAsync")]
    public static class PerPlayerLoadAsyncFix
    {
        public static async void Prefix(int playerIdentifier, IAltaFile file, IPlayer currentPlayer, object __instance)
        {
            Type tSaveFormatGeneric = __instance.GetType().GetGenericArguments()[0];
            PerPlayerContent<PlayerSave> realInstance = (PerPlayerContent<PlayerSave>)__instance;
            Hypha.Logger.Msg(ConsoleColor.Magenta, __instance.GetType().GetGenericArguments()[0].Name);

            realInstance.PlayerIdentifier = playerIdentifier;
            realInstance.File = file;
            object tSaveFormat = await AltaFileExtension.ReadAsync(file as AltaFile, tSaveFormatGeneric);
            realInstance.Content = tSaveFormat as PlayerSave;
            ModServerHandler.Current.PlayerJoined += realInstance.PlayerJoined;
            if (currentPlayer == null)
            {
                foreach (Player player in Player.AllPlayers)
                {
                    if (player.UserInfo.Identifier == playerIdentifier)
                    {
                        currentPlayer = player;
                        break;
                    }
                }
            }
            realInstance.TargetPlayer = currentPlayer;
        }
    }
}
