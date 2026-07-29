using MDS.Core;
using MDS.Systems;

namespace MDS.ConsoleCommands
{
    // rc bot act <playerId> <actionToken> [argument]
    // Dev tool: fires a single carbonPlayers playerAction at a player/bot, to confirm the exact INPUT tokens
    // for melee before the AI depends on them - e.g. does 'MeleeBlockHigh' make a bot block high, does
    // 'MeleeStrikeHigh' then 'ExecuteMeleeWeaponStrike' throw a strike. The probe showed the OUTPUT flags;
    // this confirms the tokens we SEND. No AI required - works on any spawned player id.
    public class ActSubCommand : IBotSubCommand
    {
        public BotCommandEnum SubCommandName => BotCommandEnum.Act;

        public bool Validate(string[] args, out string errorMessage)
        {
            errorMessage = string.Empty;

            if (args.Length < 2)
            {
                errorMessage = "Usage: rc bot act <playerId> <actionToken> [argument]";
                return false;
            }

            if (!int.TryParse(args[0], out _))
            {
                errorMessage = $"Invalid playerId '{args[0]}'.";
                return false;
            }

            return true;
        }

        public void Execute(int playerId, string[] args)
        {
            int targetId = int.Parse(args[0]);
            string action = args[1];

            if (args.Length > 2)
                CarbonPlayerCommands.PerformAction(targetId, action, args[2]);
            else
                CarbonPlayerCommands.PerformAction(targetId, action);

            CommandExecutor.ExecuteCommand($"serverAdmin privateMessage {playerId} Fired playerAction '{action}'{(args.Length > 2 ? $" '{args[2]}'" : "")} at player {targetId}.");
        }
    }
}
