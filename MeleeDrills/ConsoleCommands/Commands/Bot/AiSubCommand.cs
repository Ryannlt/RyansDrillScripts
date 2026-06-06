using System;
using MDS.Core;
using MDS.Systems;

namespace MDS.ConsoleCommands
{
    // rc bot ai <playerId|all> <aiType>
    public class AiSubCommand : IBotSubCommand
    {
        public BotCommandEnum SubCommandName => BotCommandEnum.Ai;

        public bool Validate(string[] args, out string errorMessage)
        {
            errorMessage = string.Empty;

            if (args.Length < 2)
            {
                errorMessage = "Usage: rc bot ai <playerId|all> <aiType>";
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

            string message;
            if (args[0].Equals("all", StringComparison.OrdinalIgnoreCase))
            {
                BotManager.SetAiAll(ai);
                message = $"Set AI '{ai}' on all bots.";
            }
            else
            {
                int id = int.Parse(args[0]);
                bool ok = BotManager.SetAi(id, ai);
                message = ok ? $"Set AI '{ai}' on bot {id}." : $"No tracked bot with id {id}.";
            }

            Logger.Log(message, LogLevel.INFO);
            CommandExecutor.ExecuteCommand($"serverAdmin privateMessage {playerId} {message}");
        }

        private static bool IsIdOrAll(string token) =>
            token.Equals("all", StringComparison.OrdinalIgnoreCase) || int.TryParse(token, out _);
    }
}
