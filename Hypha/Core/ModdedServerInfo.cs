using Alta.Api.DataTransferModels.Models.Responses;
using Alta.Api.DataTransferModels.Models.Shared;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Hypha.Core
{
    public class ModdedServerInfo : GameServerInfo
    {
        public string IP { get; set; }
        public int Port { get; set; }
        private string Path => System.IO.Path.Combine(Hypha.ServerDirectory, Name + ".svr");

        public void Serialize()
        {
            using (Stream stream = File.Open(Path, FileMode.OpenOrCreate))
            {
                using (BinaryWriter writer = new(stream, Encoding.UTF8, false))
                {
                    // Basic XOR obfuscation just to make important details less nude
                    writer.Write(IP);
                    writer.Write(Port);
                    writer.Write(Name);
                    writer.Write(Description);
                    writer.Write(Identifier);
                    writer.Write(SceneIndex);
                    writer.Write(PlayerLimit ?? 10);
                    writer.Write(Target);
                }
            }
        }

        public static ModdedServerInfo Deserialize(string path)
        {
            ModdedServerInfo result = new()
            {
                CreatedAt = DateTime.Now,
                CurrentPlayerCount = 0,
                FinalStatus = Alta.Api.DataTransferModels.Enums.GameServerStatus.Online,
                JoinType = ServerJoinType.OpenGroup,
                LastOnline = DateTime.Now,
                LastOnlinePing = DateTime.Now,
                LaunchRegion = "europe-agones",
                LastStartedVersion = Hypha.LatestVersion().ToString(),
                OnlinePlayers = Array.Empty<UserInfo>(),
                OwnerType = ServerOwnerType.World,
                Playability = 0f,
                ServerStatus = Alta.Api.DataTransferModels.Enums.GameServerStatus.Online,
                ServerType = Alta.Api.DataTransferModels.Enums.ServerType.Normal,
                TransportSystem = 1,
                Uptime = TimeSpan.MaxValue
            };

            using (Stream stream = File.Open(path, FileMode.Open))
            {
                using (BinaryReader reader = new(stream, Encoding.UTF8, false))
                {
                    result.IP = reader.ReadString();
                    result.Port = reader.ReadInt32();
                    result.Name = reader.ReadString();
                    result.Description = reader.ReadString();
                    result.Identifier = reader.ReadInt32();
                    result.SceneIndex = reader.ReadInt32();
                    result.PlayerLimit = reader.ReadInt32();
                }
            }

            return result;
        }
    }
}
