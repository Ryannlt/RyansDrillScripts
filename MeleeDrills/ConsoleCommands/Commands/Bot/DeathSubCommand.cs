using System;
using MDS.Core;
using MDS.Systems;

namespace MDS.ConsoleCommands
{
    // rc bot death <playerId|all> <policy>
    public class DeathSubCommand : IBotSubCommand
    {
        public BotCommandEnum SubCommandName => BotCommandEnum.Death;

        public bool Validate(string[] args, out string errorMessage)
        {
            errorMessage = string.Empty;

            if (args.Length < 2)
            {
                errorMessage = $"Usage: rc bot death <playerId|all> <policy>. Policies: {string.Join(", ", Enum.GetNames(typeof(BotDeathPolicy)))}.";
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

            string message;
            if (args[0].Equals("all", StringComparison.OrdinalIgnoreCase))
            {
                BotManager.SetDeathAll(policy);
                message = $"Set death policy '{policy}' on all bots.";
            }
            else
            {
                int id = int.Parse(args[0]);
                bool ok = BotManager.SetDeath(id, policy);
                message = ok ? $"Set death policy '{policy}' on bot {id}." : $"No tracked bot with id {id}.";
            }

            Logger.Log(message, LogLevel.INFO);
            CommandExecutor.ExecuteCommand($"serverAdmin privateMessage {playerId} {message}");
        }

        private static bool IsIdOrAll(string token) =>
            token.Equals("all", StringComparison.OrdinalIgnoreCase) || int.TryParse(token, out _);
    }
}
