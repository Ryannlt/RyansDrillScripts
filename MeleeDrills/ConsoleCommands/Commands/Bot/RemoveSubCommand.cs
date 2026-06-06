using System;
using MDS.Core;
using MDS.Events;

namespace MDS.ConsoleCommands
{
    // rc bot remove <playerId|all>
    public class RemoveSubCommand : IBotSubCommand
    {
        public BotCommandEnum SubCommandName => BotCommandEnum.Remove;

        public bool Validate(string[] args, out string errorMessage)
        {
            errorMessage = string.Empty;

            if (args.Length < 1 || !IsIdOrAll(args[0]))
            {
                errorMessage = "Usage: rc bot remove <playerId|all>";
                return false;
            }

            return true;
        }

        public void Execute(int playerId, string[] args)
        {
            int targetId = args[0].Equals("all", StringComparison.OrdinalIgnoreCase)
                ? -1
                : int.Parse(args[0]);

            bool success = EventDispatcher.Trigger(EventEnum.RemoveBots, new object[] { targetId }, out string errorMessage);

            if (!success)
            {
                Logger.Log($"RemoveBots failed: {errorMessage}", LogLevel.WARNING);
                CommandExecutor.ExecuteCommand($"serverAdmin privateMessage {playerId} {errorMessage}");
                return;
            }

            string message = targetId == -1 ? "Removed all bots." : $"Removed bot {targetId}.";
            CommandExecutor.ExecuteCommand($"serverAdmin privateMessage {playerId} {message}");
        }

        private static bool IsIdOrAll(string token) =>
            token.Equals("all", StringComparison.OrdinalIgnoreCase) || int.TryParse(token, out _);
    }
}
