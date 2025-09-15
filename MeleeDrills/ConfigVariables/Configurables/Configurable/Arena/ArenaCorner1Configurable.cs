using UnityEngine;
using MDS.Core;
using MDS.Systems;

namespace MDS.ConfigVariables
{
    public class ArenaCorner1Configurable : IConfigurable
    {
        public ConfigurableEnum ConfigurableName => ConfigurableEnum.ArenaCorner1;

        public bool ValidateSet(string[] args, out string errorMessage)
        {
            errorMessage = string.Empty;

            if (args.Length == 0) return true;

            if (args.Length < 2)
            {
                errorMessage = "Missing coordinates. Usage: rc set ArenaCorner1 <x> <z>";
                return false;
            }

            if (!float.TryParse(args[0], out _) || !float.TryParse(args[1], out _))
            {
                errorMessage = $"Invalid coordinate values: '{args[0]}' '{args[1]}'. Expected numbers.";
                return false;
            }

            return true;
        }

        public bool ValidateGet(string[] args, out string errorMessage)
        {
            errorMessage = string.Empty;

            if (args.Length > 0)
            {
                errorMessage = "ArenaCorner1 does not accept arguments. Usage: rc get ArenaCorner1";
                return false;
            }

            return true;
        }

        public void Set(int playerId, string[] args)
        {
            if (args.Length == 0)
            {
                string playerName = StateTracker.GetPlayerById(playerId)?.PlayerName ?? "Unknown";
                string waitingMsg = $"Awaiting position of {playerName} (ID: {playerId}) to set ArenaCorner1...";
                Logger.Log(waitingMsg, LogLevel.DEBUG);

                PlayerPacketAwaiter.WaitForPacket(playerId, packet =>
                {
                    if (packet.OwnerPosition.HasValue)
                    {
                        Vector2 pos = new Vector2(packet.OwnerPosition.Value.x, packet.OwnerPosition.Value.z);
                        ArenaManager.SetArenaCorner1(pos);

                        string msg = $"ArenaCorner1 set to your position ({pos.x:F2}, {pos.y:F2})";
                        Logger.Log(msg, LogLevel.INFO);
                        CommandExecutor.ExecuteCommand($"serverAdmin privateMessage {playerId} {msg}");
                    }
                });

                return;
            }

            float x = float.Parse(args[0]);
            float z = float.Parse(args[1]);

            ArenaManager.SetArenaCorner1(new Vector2(x, z));

            string message = $"ArenaCorner1 set to ({x:F2}, {z:F2})";
            Logger.Log(message, LogLevel.INFO);
            CommandExecutor.ExecuteCommand($"serverAdmin privateMessage {playerId} {message}");
        }

        public void Get(int playerId, string[] args)
        {
            if (ArenaManager.Arena == null)
            {
                string message = "Arena has not been initialized.";
                Logger.Log(message, LogLevel.WARNING);
                CommandExecutor.ExecuteCommand($"serverAdmin privateMessage {playerId} {message}");
                return;
            }

            Vector2 c1 = ArenaManager.Arena.Corner1;
            string info = $"ArenaCorner1 is at ({c1.x:F2}, {c1.y:F2})";
            Logger.Log(info, LogLevel.INFO);
            CommandExecutor.ExecuteCommand($"serverAdmin privateMessage {playerId} {info}");
        }
    }
}
