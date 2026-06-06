using System;
using MDS.Core;
using MDS.Events;
using MDS.Systems;

namespace MDS.ConsoleCommands
{
    // rc bot setBotAi <playerId|all> <aiType>
    public class SetBotAiSubCommand : IBotSubCommand
    {
        public BotCommandEnum SubCommandName => BotCommandEnum.SetBotAi;

        public bool Validate(string[] args, out string errorMessage)
        {
            errorMessage = string.Empty;

            if (args.Length < 2)
            {
                errorMessage = "Usage: rc bot setBotAi <playerId|all> <aiType>";
                return false;
            }

            if (!IsIdOrAll(args[0]))
            {
                errorMessage = $"Invalid target '{args[0]}'. Must be a playerId or 'all'.";
                return false;
            }

            if (!EnumParser.TryParseEnumStrict(args[1], out BotAiEnum _))
            {
                errorMessage = $"Unknown AI '{args[1]}'. Valid: {string.Join(", ", Enum.GetNames(typeof(BotAiEnum)))}.";
                return false;
            }

            return true;
        }

        public void Execute(int playerId, string[] args)
        {
            EnumParser.TryParseEnumStrict(args[1], out BotAiEnum ai);
            int targetId = args[0].Equals("all", StringComparison.OrdinalIgnoreCase) ? -1 : int.Parse(args[0]);

            bool success = EventDispatcher.Trigger(EventEnum.SetBotAi, new object[] { targetId, ai }, out string error);

            if (!success)
            {
                Logger.Log($"SetBotAi failed: {error}", LogLevel.WARNING);
                CommandExecutor.ExecuteCommand($"serverAdmin privateMessage {playerId} {error}");
                return;
            }

            string message = targetId == -1 ? $"Set AI '{ai}' on all bots." : $"Set AI '{ai}' on bot {targetId}.";
            CommandExecutor.ExecuteCommand($"serverAdmin privateMessage {playerId} {message}");
        }

        private static bool IsIdOrAll(string token) =>
            token.Equals("all", StringComparison.OrdinalIgnoreCase) || int.TryParse(token, out _);
    }
}
