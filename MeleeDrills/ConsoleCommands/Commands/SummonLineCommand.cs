using UnityEngine;
using MDS.Core;
using MDS.Systems;

namespace MDS.ConsoleCommands
{
    // rc summonLine [count] [faction class] [ai] [death] [name [regtag [uniformId]]]
    // Forms a shoulder-to-shoulder line of bots centred on the caller, facing the caller's direction.
    public class SummonLineCommand : IConsoleCommand
    {
        public ConsoleCommandEnum CommandName => ConsoleCommandEnum.SummonLine;

        public bool Validate(string[] parameters, out string errorMessage) =>
            LineArgs.ValidateTail(parameters, out errorMessage);

        public void Execute(int playerId, string[] parameters)
        {
            if (!LineArgs.ResolveTail(parameters, playerId, out int count, out var spec, out string error))
            {
                CommandExecutor.ExecuteCommand($"serverAdmin privateMessage {playerId} {error}");
                return;
            }

            var caller = StateTracker.GetPlayerById(playerId);
            if (caller?.PlayerObject == null)
            {
                CommandExecutor.ExecuteCommand($"serverAdmin privateMessage {playerId} Cannot summon line - your position is unavailable (are you spawned?).");
                return;
            }

            Transform t = caller.PlayerObject.transform;
            Vector2 center = new Vector2(t.position.x, t.position.z);
            float rotation = t.eulerAngles.y;

            LineArgs.Trigger(playerId, center, rotation, count, spec);
        }
    }
}
