using UnityEngine;
using MDS.ConfigVariables;
using MDS.Core;
using MDS.Events;

namespace MDS.ConsoleCommands
{
    // Shared parsing/dispatch for the '[count] [spec...]' tail of summonLine / spawnLine.
    // count: optional leading positive int (overrides the lineBotCount config default).
    // spec : reuses BotSpawnArgs (faction/class default to caller; ai/death to config defaults).
    public static class LineArgs
    {
        public static bool ValidateTail(string[] tail, out string error)
        {
            if (!StripCount(tail, out string[] specArgs, out _, out error))
                return false;

            return BotSpawnArgs.ValidateShape(specArgs, allowCount: false, out error);
        }

        public static bool ResolveTail(string[] tail, int callerPlayerId, out int count, out BotSpawnArgs spec, out string error)
        {
            spec = null;

            if (!StripCount(tail, out string[] specArgs, out count, out error))
                return false;

            return BotSpawnArgs.TryResolve(specArgs, callerPlayerId, allowCount: false, out spec, out error);
        }

        // Builds the line params from the resolved spec + config spacing and triggers SpawnLineEvent.
        public static void Trigger(int playerId, Vector2 center, float rotation, int count, BotSpawnArgs spec)
        {
            float spacing = ((LineSpacingConfigurable)ConfigurableRegistry.Get(ConfigurableEnum.LineSpacing)).LineSpacing;

            bool success = EventDispatcher.Trigger(EventEnum.SpawnLine,
                new object[] { center, rotation, count, spacing, spec.Spec, spec.Ai, spec.Death },
                out string error);

            if (!success)
            {
                Logger.Log($"SpawnLine failed: {error}", LogLevel.WARNING);
                CommandExecutor.ExecuteCommand($"serverAdmin privateMessage {playerId} {error}");
                return;
            }

            CommandExecutor.ExecuteCommand($"serverAdmin privateMessage {playerId} Forming line of {count} bot(s): {spec.Spec.Faction}/{spec.Spec.Class}, AI {spec.Ai}, death {spec.Death}.");
        }

        // Pulls an optional leading positive-int count; defaults to the lineBotCount configurable.
        private static bool StripCount(string[] tail, out string[] specArgs, out int count, out string error)
        {
            error = string.Empty;
            specArgs = tail;
            count = ((LineBotCountConfigurable)ConfigurableRegistry.Get(ConfigurableEnum.LineBotCount)).LineBotCount;

            if (tail.Length > 0 && int.TryParse(tail[0], out int c))
            {
                if (c <= 0) { error = "Line count must be a positive integer."; return false; }
                count = c;
                specArgs = tail[1..];
            }

            return true;
        }
    }
}
