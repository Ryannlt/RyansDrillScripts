using UnityEngine;
using MDS.Core;
using MDS.Systems;

namespace MDS.Events
{
    // Command-facing trigger for a bot line. Validates the param shape then delegates the actual
    // geometry/spawn to LineManager.SpawnLine (shared with the map-load auto-populate path).
    // Parameters: (Vector2 center, float rotation, int count, float spacing, BotSpawnSpec spec, BotAiEnum ai, BotDeathPolicy death)
    public class SpawnLineEvent : IEvent
    {
        public EventEnum EventName => EventEnum.SpawnLine;

        public bool Validate(object[] parameters, out string errorMessage)
        {
            errorMessage = string.Empty;

            if (parameters.Length != 7 ||
                parameters[0] is not Vector2 ||
                parameters[1] is not float ||
                parameters[2] is not int count ||
                parameters[3] is not float spacing ||
                parameters[4] is not BotSpawnSpec ||
                parameters[5] is not BotAiEnum ai ||
                parameters[6] is not BotDeathPolicy)
            {
                errorMessage = "Invalid parameters. Expected: (Vector2 center, float rotation, int count, float spacing, BotSpawnSpec, BotAiEnum, BotDeathPolicy).";
                return false;
            }

            if (count <= 0) { errorMessage = "Line count must be greater than 0."; return false; }
            if (spacing <= 0f) { errorMessage = "Line spacing must be greater than 0."; return false; }
            if (!BotAiFactory.IsRegistered(ai)) { errorMessage = $"AI type '{ai}' is not registered."; return false; }

            return true;
        }

        public void Trigger(object[] parameters)
        {
            Vector2 center = (Vector2)parameters[0];
            float rotation = (float)parameters[1];
            int count = (int)parameters[2];
            float spacing = (float)parameters[3];
            var spec = (BotSpawnSpec)parameters[4];
            var ai = (BotAiEnum)parameters[5];
            var death = (BotDeathPolicy)parameters[6];

            LineManager.SpawnLine(center, rotation, count, spacing, spec, ai, death);
        }
    }
}
