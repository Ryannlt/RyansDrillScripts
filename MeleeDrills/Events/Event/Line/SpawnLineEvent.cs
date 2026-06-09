using System.Collections.Generic;
using UnityEngine;
using MDS.Core;
using MDS.Systems;

namespace MDS.Events
{
    // Spawns a shoulder-to-shoulder line of bots centred on 'center', all facing 'rotation' (deg from
    // North). Caller-agnostic and reusable (summonLine/spawnLine commands + future map-load populate).
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

            // 'right' is perpendicular to the facing (the line runs along it); every bot faces 'rotation'.
            // Heading is degrees from North to match inputRotation - verify in-game with summon facing.
            float rad = rotation * Mathf.Deg2Rad;
            Vector2 right = new Vector2(Mathf.Cos(rad), -Mathf.Sin(rad));
            float startOffset = -((count - 1) * spacing) / 2f;

            var placements = new List<BotPlacement>(count);
            for (int i = 0; i < count; i++)
            {
                Vector2 pos2D = center + right * (startOffset + i * spacing);
                float y = TerrainSampler.GetYAt(pos2D);
                placements.Add(new BotPlacement(new Vector3(pos2D.x, y, pos2D.y), rotation));
            }

            BotManager.SpawnBotsAt(placements, spec, ai, death);
            Logger.Log($"SpawnLineEvent: {count} bots, spacing {spacing:F2}, facing {rotation:F0} deg.", LogLevel.INFO);
        }
    }
}
