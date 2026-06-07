using UnityEngine;
using MDS.Core;
using MDS.Events;
using MDS.Systems;

namespace MDS.ConsoleCommands
{
    // rc bot summon [faction class] [ai] [death] [name [regtag [uniformId]]]
    // Spawns a single bot (no count - multiple would stack on one spot) then teleports it to the caller.
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

            var caller = StateTracker.GetPlayerById(playerId);
            if (caller?.PlayerObject == null)
            {
                CommandExecutor.ExecuteCommand($"serverAdmin privateMessage {playerId} Cannot summon - your position is unavailable (are you spawned?).");
                return;
            }
            Transform callerTransform = caller.PlayerObject.transform;
            var placement = new BotPlacement(callerTransform.position, callerTransform.eulerAngles.y);

            bool success = EventDispatcher.Trigger(EventEnum.SpawnBots,
                new object[] { parsed.Spec, parsed.Count, parsed.Ai, parsed.Death, placement },
                out string eventError);

            if (!success)
            {
                Logger.Log($"Summon failed: {eventError}", LogLevel.WARNING);
                CommandExecutor.ExecuteCommand($"serverAdmin privateMessage {playerId} {eventError}");
                return;
            }

            CommandExecutor.ExecuteCommand($"serverAdmin privateMessage {playerId} Summoning bot: {parsed.Spec.Faction}/{parsed.Spec.Class}, AI {parsed.Ai}, death {parsed.Death}.");
        }
    }
}
