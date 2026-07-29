using MDS.Core;
using MDS.Events;
using MDS.Systems;

namespace MDS.ConsoleCommands
{
    // rc bot summon [faction class] [ai] [death] [name [regtag [uniformId]]] [at <playerId>]
    // Spawns a single bot (no count - multiple would stack on one spot) then teleports it to the caller.
    // 'at <playerId>' places it at that player instead - the usable route while in free roam, where the
    // server never learns the caller's own position. Faction/class still default to the CALLER's.
    public class SummonSubCommand : IBotSubCommand
    {
        public BotCommandEnum SubCommandName => BotCommandEnum.Summon;

        public bool Validate(string[] args, out string errorMessage)
        {
            if (!BotSpawnArgs.StripAtTarget(args, out string[] rest, out _, out errorMessage))
                return false;

            return BotSpawnArgs.ValidateShape(rest, allowCount: false, out errorMessage);
        }

        public void Execute(int playerId, string[] args)
        {
            if (!BotSpawnArgs.StripAtTarget(args, out string[] rest, out int? targetPlayerId, out string atError))
            {
                CommandExecutor.ExecuteCommand($"serverAdmin privateMessage {playerId} {atError}");
                return;
            }

            if (!BotSpawnArgs.TryResolve(rest, playerId, allowCount: false, out var parsed, out string error))
            {
                CommandExecutor.ExecuteCommand($"serverAdmin privateMessage {playerId} {error}");
                return;
            }

            // Where to put the bot: the named 'at' player, else the caller's body when embodied, else their
            // free-roam viewpoint (the corpse would otherwise be used).
            SummonOrigin.Resolve(playerId, targetPlayerId,
                placement =>
                {
                    bool success = EventDispatcher.Trigger(EventEnum.SpawnBots,
                        new object[] { parsed.Spec, parsed.Count, parsed.Ai, parsed.Death, placement },
                        out string eventError);

                    if (!success)
                    {
                        Logger.Log($"Summon failed: {eventError}", LogLevel.WARNING);
                        CommandExecutor.ExecuteCommand($"serverAdmin privateMessage {playerId} {eventError}");
                        return;
                    }

                    CommandExecutor.ExecuteCommand($"serverAdmin privateMessage {playerId} Summoning bot: {FactionTokens.DisplayName(parsed.Spec.Faction)}/{parsed.Spec.Class}, AI {parsed.Ai}, death {parsed.Death}.");
                },
                reason => CommandExecutor.ExecuteCommand($"serverAdmin privateMessage {playerId} {reason}"));
        }
    }
}
