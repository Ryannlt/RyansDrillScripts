using System;
using MDS.Core;
using MDS.Events;
using MDS.Systems;

namespace MDS.ConsoleCommands
{
    // rc bot setBotDeathPolicy <playerId|all> <policy>
    public class SetBotDeathPolicySubCommand : IBotSubCommand
    {
        public BotCommandEnum SubCommandName => BotCommandEnum.SetBotDeathPolicy;

        public bool Validate(string[] args, out string errorMessage)
        {
            errorMessage = string.Empty;

            if (args.Length < 2)
            {
                errorMessage = $"Usage: rc bot setBotDeathPolicy <playerId|all> <policy>. Policies: {string.Join(", ", Enum.GetNames(typeof(BotDeathPolicy)))}.";
                return false;
            }

            if (!IsIdOrAll(args[0]))
            {
                errorMessage = $"Invalid target '{args[0]}'. Must be a playerId or 'all'.";
                return false;
            }

            if (!EnumParser.TryParseEnumStrict(args[1], out BotDeathPolicy _))
            {
                errorMessage = $"Unknown policy '{args[1]}'. Valid: {string.Join(", ", Enum.GetNames(typeof(BotDeathPolicy)))}.";
                return false;
            }

            return true;
        }

        public void Execute(int playerId, string[] args)
        {
            EnumParser.TryParseEnumStrict(args[1], out BotDeathPolicy policy);
            int targetId = args[0].Equals("all", StringComparison.OrdinalIgnoreCase) ? -1 : int.Parse(args[0]);

            bool success = EventDispatcher.Trigger(EventEnum.SetBotDeathPolicy, new object[] { targetId, policy }, out string error);

            if (!success)
            {
                Logger.Log($"SetBotDeathPolicy failed: {error}", LogLevel.WARNING);
                CommandExecutor.ExecuteCommand($"serverAdmin privateMessage {playerId} {error}");
                return;
            }

            string message = targetId == -1 ? $"Set death policy '{policy}' on all bots." : $"Set death policy '{policy}' on bot {targetId}.";
            CommandExecutor.ExecuteCommand($"serverAdmin privateMessage {playerId} {message}");
        }

        private static bool IsIdOrAll(string token) =>
            token.Equals("all", StringComparison.OrdinalIgnoreCase) || int.TryParse(token, out _);
    }
}
