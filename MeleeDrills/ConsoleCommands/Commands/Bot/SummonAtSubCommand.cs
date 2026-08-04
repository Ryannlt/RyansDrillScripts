using MDS.Core;
using MDS.Events;
using MDS.Systems;

namespace MDS.ConsoleCommands
{
    // rc bot summonAt <playerId> [faction class] [ai] [death] [name [regtag [uniformId]]]
    // Spawns a single bot at another player's position, facing the way they face. This is the route that works
    // from free roam, where the server never learns the caller's own position, which is the usual reason to reach
    // for it. Faction and class default to the TARGET player's rather than the caller's, so summoning a sparring
    // partner onto someone needs nothing but their id. Everything after the id follows the same positional
    // grammar as 'rc bot summon'. All replies, including failures, go to the caller, not the target.
    public class SummonAtSubCommand : IBotSubCommand
    {
        public BotCommandEnum SubCommandName => BotCommandEnum.SummonAt;

        public bool Validate(string[] args, out string errorMessage)
        {
            errorMessage = string.Empty;

            if (args.Length < 1 || !int.TryParse(args[0], out _))
            {
                errorMessage = "Usage: rc bot summonAt <playerId> [faction class] [ai] [death] [name [regtag [uniformId]]]";
                return false;
            }

            return BotSpawnArgs.ValidateShape(args[1..], allowCount: false, out errorMessage);
        }

        public void Execute(int playerId, string[] args)
        {
            int targetPlayerId = int.Parse(args[0]);
            string[] rest = args[1..];

            // Resolve the placement first: it reports a missing or unspawned target in its own words, and it
            // guarantees the target is embodied, so the faction and class we copy below are actually there.
            SummonOrigin.ResolveAtPlayer(targetPlayerId,
                placement =>
                {
                    if (!BotSpawnArgs.TryResolve(rest, targetPlayerId, allowCount: false, out var parsed, out string error))
                    {
                        CommandExecutor.ExecuteCommand($"serverAdmin privateMessage {playerId} {error}");
                        return;
                    }

                    // The target doubles as the guard target, so 'summonAt <id> ... Guardian' is a bodyguard for
                    // that player with nothing else to set up.
                    bool success = EventDispatcher.Trigger(EventEnum.SpawnBots,
                        new object[] { parsed.Spec, parsed.Count, parsed.Ai, parsed.Death, placement, targetPlayerId },
                        out string eventError);

                    if (!success)
                    {
                        Logger.Log($"SummonAt failed: {eventError}", LogLevel.WARNING);
                        CommandExecutor.ExecuteCommand($"serverAdmin privateMessage {playerId} {eventError}");
                        return;
                    }

                    CommandExecutor.ExecuteCommand($"serverAdmin privateMessage {playerId} Summoning bot at player {targetPlayerId}: {FactionTokens.DisplayName(parsed.Spec.Faction)}/{parsed.Spec.Class}, AI {parsed.Ai}, death {parsed.Death}.");
                },
                reason => CommandExecutor.ExecuteCommand($"serverAdmin privateMessage {playerId} {reason}"));
        }
    }
}
