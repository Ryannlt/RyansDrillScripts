using UnityEngine;
using MDS.Core;
using MDS.Events;
using MDS.Systems;

namespace MDS.ConsoleCommands
{
    // rc bot summon [count] [faction class] [ai] [death] [name [regtag [uniformId]]]
    // Same as spawn, then teleports the spawned bot(s) to the caller's location.
    public class SummonSubCommand : IBotSubCommand
    {
        public BotCommandEnum SubCommandName => BotCommandEnum.Summon;

        public bool Validate(string[] args, out string errorMessage) =>
            BotSpawnArgs.ValidateShape(args, out errorMessage);

        public void Execute(int playerId, string[] args)
        {
            if (!BotSpawnArgs.TryResolve(args, playerId, out var parsed, out string error))
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
            Vector3 position = caller.PlayerObject.transform.position;

            bool success = EventDispatcher.Trigger(EventEnum.SpawnBots,
                new object[] { parsed.Spec, parsed.Count, parsed.Ai, parsed.Death, position },
                out string eventError);

            if (!success)
            {
                Logger.Log($"Summon failed: {eventError}", LogLevel.WARNING);
                CommandExecutor.ExecuteCommand($"serverAdmin privateMessage {playerId} {eventError}");
                return;
            }

            CommandExecutor.ExecuteCommand($"serverAdmin privateMessage {playerId} Summoning {parsed.Count} bot(s) to your location.");
        }
    }
}
