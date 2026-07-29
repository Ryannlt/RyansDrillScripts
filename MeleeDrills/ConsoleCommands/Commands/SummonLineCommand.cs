using UnityEngine;
using MDS.Core;
using MDS.Systems;

namespace MDS.ConsoleCommands
{
    // rc summonLine [count] [faction class] [ai] [death] [name [regtag [uniformId]]] [at <playerId>]
    // Forms a shoulder-to-shoulder line of bots centred on the caller, facing the caller's direction.
    // 'at <playerId>' centres it on that player instead - the usable route while in free roam, where the
    // server never learns the caller's own position. Faction/class still default to the CALLER's.
    public class SummonLineCommand : IConsoleCommand
    {
        public ConsoleCommandEnum CommandName => ConsoleCommandEnum.SummonLine;

        public bool Validate(string[] parameters, out string errorMessage)
        {
            if (!BotSpawnArgs.StripAtTarget(parameters, out string[] rest, out _, out errorMessage))
                return false;

            return LineArgs.ValidateTail(rest, out errorMessage);
        }

        public void Execute(int playerId, string[] parameters)
        {
            if (!BotSpawnArgs.StripAtTarget(parameters, out string[] rest, out int? targetPlayerId, out string atError))
            {
                CommandExecutor.ExecuteCommand($"serverAdmin privateMessage {playerId} {atError}");
                return;
            }

            if (!LineArgs.ResolveTail(rest, playerId, out int count, out var spec, out string error))
            {
                CommandExecutor.ExecuteCommand($"serverAdmin privateMessage {playerId} {error}");
                return;
            }

            // Centre on the named 'at' player, else the caller's body when embodied, else their free-roam
            // viewpoint (the corpse would otherwise be used).
            SummonOrigin.Resolve(playerId, targetPlayerId,
                placement =>
                {
                    Vector2 center = new Vector2(placement.Position.x, placement.Position.z);
                    LineArgs.Trigger(playerId, center, placement.Heading ?? 0f, count, spec);
                },
                reason => CommandExecutor.ExecuteCommand($"serverAdmin privateMessage {playerId} {reason}"));
        }
    }
}
