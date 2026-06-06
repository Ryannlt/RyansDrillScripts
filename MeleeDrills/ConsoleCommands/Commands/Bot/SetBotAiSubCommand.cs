using System;
using MDS.Core;
using MDS.Events;
using MDS.Systems;

namespace MDS.ConsoleCommands
{
    // rc bot setBotAi <playerId|all|attacking|defending|faction> <aiType>
    public class SetBotAiSubCommand : IBotSubCommand
    {
        public BotCommandEnum SubCommandName => BotCommandEnum.SetBotAi;

        public bool Validate(string[] args, out string errorMessage)
        {
            errorMessage = string.Empty;

            if (args.Length < 2)
            {
                errorMessage = "Usage: rc bot setBotAi <playerId|all|attacking|defending|faction> <aiType>";
                return false;
            }

            if (!BotTargetSelector.IsValidToken(args[0]))
            {
                errorMessage = $"Invalid target '{args[0]}'. Use a playerId, all, attacking, defending, or a faction name.";
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
            string target = args[0];
            EnumParser.TryParseEnumStrict(args[1], out BotAiEnum ai);

            bool success = EventDispatcher.Trigger(EventEnum.SetBotAi, new object[] { target, ai }, out string error);

            if (!success)
            {
                Logger.Log($"SetBotAi failed: {error}", LogLevel.WARNING);
                CommandExecutor.ExecuteCommand($"serverAdmin privateMessage {playerId} {error}");
                return;
            }

            CommandExecutor.ExecuteCommand($"serverAdmin privateMessage {playerId} Set AI '{ai}' on bots matching '{target}'.");
        }
    }
}
