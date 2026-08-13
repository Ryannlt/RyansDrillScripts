using MDS.Core;
using MDS.Events;
using MDS.Systems;

namespace MDS.ConsoleCommands
{
    // rc bot summon [faction class] [ai] [death] [name [regtag [uniformId]]]
    // Spawns a single bot (no count, since multiple would stack on one spot) then teleports it to the caller.
    // Faction and class default to the caller's. To place the bot at another player instead, which is the route
    // that works from free roam, use 'rc bot summonAt <playerId>'.
    public class SummonSubCommand : IBotSubCommand
    {
        public BotCommandEnum SubCommandName => BotCommandEnum.Summon;

        public bool Validate(string[] args, out string errorMessage) =>
            BotSpawnArgs.ValidateShape(args, allowCount: false, out errorMessage);

        public void Execute(int playerId, string[] args)
        {
            if (!BotSpawnArgs.TryResolve(args, playerId, allowCount: false, out var parsed, out string error))
            {
                CommandExecutor.ExecuteCommand($"serverAdmin privateMessage {playerId} {error}");
                return;
            }

            // The caller's body when embodied, else their free-roam viewpoint (the corpse would otherwise be used).
            SummonOrigin.Resolve(playerId,
                placement =>
                {
                    // The caller doubles as the guard target, so 'summon ... Guardian' escorts whoever summoned it.
                    bool success = EventDispatcher.Trigger(EventEnum.SpawnBots,
                        new object[] { parsed.Spec, parsed.Count, parsed.Ai, parsed.Death, placement, playerId },
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
