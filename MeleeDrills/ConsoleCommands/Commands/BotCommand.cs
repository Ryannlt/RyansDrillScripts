using System;
using MDS.Core;

// rc bot <spawn|ai|remove|list> [args]
// Thin dispatcher: parses the subcommand keyword to a BotCommandEnum, looks up its handler in the
// BotSubCommandRegistry, and delegates validation/execution - mirroring how Get/Set delegate to
// configurables. Each subcommand owns its own validation in its own file (Commands/Bot/).

namespace MDS.ConsoleCommands
{
    public class BotCommand : IConsoleCommand
    {
        public ConsoleCommandEnum CommandName => ConsoleCommandEnum.Bot;

        public bool Validate(string[] parameters, out string errorMessage)
        {
            errorMessage = string.Empty;

            if (parameters.Length < 1)
            {
                errorMessage = "Missing subcommand. Usage: rc bot <spawn|ai|remove|list>";
                return false;
            }

            if (!EnumParser.TryParseEnumStrict(parameters[0], out BotCommandEnum parsedSub))
            {
                errorMessage = $"Unknown subcommand '{parameters[0]}'. Usage: rc bot <spawn|ai|remove|list>";
                return false;
            }

            if (!BotSubCommandRegistry.TryGet(parsedSub, out var subCommand))
            {
                errorMessage = $"Bot sub-command '{parsedSub}' is not registered.";
                return false;
            }

            string[] args = parameters.Length > 1 ? parameters[1..] : Array.Empty<string>();
            return subCommand.Validate(args, out errorMessage);
        }

        public void Execute(int playerId, string[] parameters)
        {
            if (!EnumParser.TryParseEnumStrict(parameters[0], out BotCommandEnum parsedSub)) return;
            if (!BotSubCommandRegistry.TryGet(parsedSub, out var subCommand)) return;

            string[] args = parameters.Length > 1 ? parameters[1..] : Array.Empty<string>();
            subCommand.Execute(playerId, args);
        }
    }
}
