using Alta.Api.Client.HighLevel;
using Alta.Api.DataTransferModels.Models.Responses;
using Alta.Api.DataTransferModels.Models.Shared;
using Alta.Character;
using Alta.Chunks;
using Alta.Map;
using Alta.Networking;
using Alta.Utilities;
using CrossGameplayApi;
using HarmonyLib;
using Hypha.Core;
using Hypha.Helpers;
using MelonLoader;
using Mono.Cecil;
using MonoMod.Cil;
using MonoMod.RuntimeDetour;
using MonoMod.Utils;
using Newtonsoft.Json;
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
        public static ModdedServerInfo TemplateServerInfo { get; internal set; }
        public static ModdedServerInfo ServerToHost { get; internal set; } // Implement properly
        public static RequestJoinMessage StaticJoinMessage { get; internal set; }

        internal List<ModdedServerInfo> collectedServers;
        internal static string RootDirectory => Directory.GetParent(Application.dataPath).FullName;
        internal static string ServerDirectory => Path.Combine(RootDirectory, "Modded Servers");
        public static List<ModdedServerInfo> moddedServers;

        public static event Action OnPrefabWarmup;


        public override async void OnApplicationStarted()
        {
            StaticJoinMessage = new();
            Logger ??= LoggerInstance;

            MethodBase listModServers = typeof(ServerBoard).GetMethod("RefreshServersList", BindingFlags.Public | BindingFlags.Instance).GetStateMachineTarget();

            new ILHook(listModServers, il =>
            {
                ILCursor ilCursor = new ILCursor(il);
                ilCursor.GotoNext(MoveType.After,
                    x => x.MatchCall<Application>("get_isPlaying"));

                ilCursor.Emit(Mono.Cecil.Cil.OpCodes.Ldloc_1);
                ilCursor.Emit(Mono.Cecil.Cil.OpCodes.Callvirt, typeof(Hypha).GetMethod(nameof(LoadOnlineServers)));
            });

            ItemAPI.Init();

            foreach (string parameter in Environment.GetCommandLineArgs())
            {
                if (parameter == "$ServerMode")
                {
                    IsServerInstance = true;
                    break;
                }
            }

            IPAddress externalIP = await GetExternalIpAddress();

            TemplateServerInfo = new ModdedServerInfo()
            {
                CreatedAt = DateTime.Now,
                CurrentPlayerCount = 0,
                Description = "This is your modded server!",
                FinalStatus = Alta.Api.DataTransferModels.Enums.GameServerStatus.Online,
                Identifier = 2068646221,
                JoinType = ServerJoinType.OpenGroup,
                LastOnline = DateTime.Now,
                LastOnlinePing = DateTime.Now,
                LaunchRegion = "europe-agones",
                LastStartedVersion = "main-1.7.2.1.42203",
                Name = "A Modded Tale",
                OnlinePlayers = Array.Empty<UserInfo>(),
                OwnerType = ServerOwnerType.World,
                Playability = 0f,
                PlayerLimit = 50,
                SceneIndex = 0,
                ServerStatus = Alta.Api.DataTransferModels.Enums.GameServerStatus.Online,
                ServerType = Alta.Api.DataTransferModels.Enums.ServerType.Normal,
                Target = 1,
                TransportSystem = 1,
                Uptime = TimeSpan.MaxValue,
                IP = externalIP.ToString(),
                Port = 1757
            };

            ServerToHost = TemplateServerInfo;
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
                AltaSceneManager.LoadMainMenuSceneAsync();
                StartModdedServer(new ModdedServerAccess(), false, false, 1757, true);
            }

            SceneManager.sceneLoaded += (scene, loadMode) =>
            {
                if (scene.name == "Main Menu")
                {
                    if (IsServerInstance)
                    {
                        StartModdedServer(new ModdedServerAccess(), false, false, 1757, true);
                        return;
                    }

                    moddedServers = FetchAllLocalServers();
                }
            };
        }

        public static void LoadOnlineServers(ServerBoard board)
        {
            if (board.type == ServerBoardType.MyServers)
            {
                List<GameServerInfo> currentServers = board.lastReceivedServers.ToList();

                for (int i = 0; i < moddedServers.Count; i++)
                {
                    Logger.Msg(ConsoleColor.Magenta, "Modded server found! Name is " + moddedServers[i].Name);
                    currentServers.Add(moddedServers[i]);
                }

                board.lastReceivedServers = currentServers;
            }
        }

        public override void OnGUI()
        {
            if (GUILayout.Button("Start initial server"))
            {
                IServerAccess access = new ModdedServerAccess();
                LaunchNewServerInstance(access, true, 1757);
            }

            if (GUILayout.Button("Serialize test server"))
            {
                TemplateServerInfo.Serialize();
            }

            if (GUILayout.Button("Join modded server"))
            {
                VrMainMenu.Instance.JoinServer(ServerToHost);
            }
        }


        public List<ModdedServerInfo> FetchAllLocalServers()
        {
            List<ModdedServerInfo> temp = new();

            if (!Directory.Exists(ServerDirectory)) Directory.CreateDirectory(ServerDirectory);
            string[] serverDirectories = Directory.GetFiles(ServerDirectory, "*.svr");

            for (int i = 0; i < serverDirectories.Length; i++)
            {
                temp.Add(ModdedServerInfo.Deserialize(serverDirectories[i]));
            }

            return temp;
        }




        // https://stackoverflow.com/a/21771432
        public static async Task<IPAddress> GetExternalIpAddress()
        {
            string externalIP = (await new HttpClient().GetStringAsync("http://icanhazip.com")).Replace("\\r\\n", "").Replace("\\n", "").Trim();
            if (!IPAddress.TryParse(externalIP, out var ipAddress)) return null;
            return ipAddress;
        }


        public void SerializeServer(ModdedServerInfo serverInfo)
        {
            File.WriteAllText(Path.Combine(ServerDirectory, serverInfo.Name) + ".svr", JsonConvert.SerializeObject(serverInfo, Formatting.Indented));
        }


        public static async Task<bool> StartModdedServer(IServerAccess access, bool headless = true, bool externalLaunch = true, int port = 1757, bool runningLocally = true)
        {
            return await GameModeManager.StartGameModeAsync(new ModdedServerGamemode(access, headless, externalLaunch, port, runningLocally));
        }


        public static GameVersion LatestVersion()
        {
            GenericVersionParts genericVersionParts = VersionHelper.Parse(BuildVersion.CurrentVersion.ToString());
            return new GameVersion(genericVersionParts.Stream, genericVersionParts.Season, genericVersionParts.Major, genericVersionParts.Minor, genericVersionParts.ChangeSet);
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
