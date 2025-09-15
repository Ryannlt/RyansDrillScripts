using UnityEngine;
using MDS.Core;
using MDS.Systems;

namespace MDS.ConfigVariables
{
    public class ArenaCorner2Configurable : IConfigurable
    {
        public ConfigurableEnum ConfigurableName => ConfigurableEnum.ArenaCorner2;

        public bool ValidateSet(string[] args, out string errorMessage)
        {
            errorMessage = string.Empty;

            if (args.Length == 0) return true;

            if (args.Length < 2)
            {
                errorMessage = "Missing coordinates. Usage: rc set ArenaCorner2 <x> <z>";
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
                errorMessage = "ArenaCorner2 does not accept arguments. Usage: rc get ArenaCorner2";
                return false;
            }

            return true;
        }

        public void Set(int playerId, string[] args)
        {
            if (args.Length == 0)
            {
                string playerName = StateTracker.GetPlayerById(playerId)?.PlayerName ?? "Unknown";
                string waitingMsg = $"Awaiting position of {playerName} (ID: {playerId}) to set ArenaCorner2...";
                Logger.Log(waitingMsg, LogLevel.DEBUG);

                PlayerPacketAwaiter.WaitForPacket(playerId, packet =>
                {
                    if (packet.OwnerPosition.HasValue)
                    {
                        Vector2 pos = new Vector2(packet.OwnerPosition.Value.x, packet.OwnerPosition.Value.z);
                        ArenaManager.SetArenaCorner2(pos);

                        string msg = $"ArenaCorner2 set to your position ({pos.x:F2}, {pos.y:F2})";
                        Logger.Log(msg, LogLevel.INFO);
                        CommandExecutor.ExecuteCommand($"serverAdmin privateMessage {playerId} {msg}");
                    }
                });

                return;
            }

            float x = float.Parse(args[0]);
            float z = float.Parse(args[1]);

            ArenaManager.SetArenaCorner2(new Vector2(x, z));

            string message = $"ArenaCorner2 set to ({x:F2}, {z:F2})";
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

            Vector2 c2 = ArenaManager.Arena.Corner2;
            string info = $"ArenaCorner2 is at ({c2.x:F2}, {c2.y:F2})";
            Logger.Log(info, LogLevel.INFO);
            CommandExecutor.ExecuteCommand($"serverAdmin privateMessage {playerId} {info}");
        }
    }
}
