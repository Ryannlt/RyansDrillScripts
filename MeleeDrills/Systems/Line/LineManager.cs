using System.Collections;
using System.Collections.Generic;
using HoldfastSharedMethods;
using UnityEngine;
using MDS.ConfigVariables;
using MDS.Core;

// Owns the bot-line geometry and the map-load auto-populate queue.
//
//  - SpawnLine(...) is the single reusable formation builder (called by SpawnLineEvent for the runtime
//    summonLine/spawnLine commands, and directly here for staged map-load lines). Keeping the geometry
//    in the Systems layer lets map-load spawn lines without the Systems layer dispatching events.
//  - Staged lines come from the 'SpawnLine' config variable (PassConfigVariables). They are held until
//    the round begins (StateTracker.OnRoundDetails -> SpawnStagedLines) and replayed every round.

namespace MDS.Systems
{
    public static class LineManager
    {
        // Bots are requested shortly AFTER the round starts so the world/terrain/spawn system is ready
        // (OnRoundDetails can fire a touch before spawning is accepted). Failed spawns self-heal via the
        // BotManager ghost timeout, so this only needs to be "usually enough".
        private const float SpawnDelaySeconds = 3f;

        private static readonly List<StagedLine> _staged = new();

        public static IReadOnlyList<StagedLine> StagedLines => _staged;

        // ---- Reusable formation builder (command path + map-load path) ----

        // Spawns a shoulder-to-shoulder line of bots centred on 'center', all facing 'rotation' (deg from
        // North). 'right' is perpendicular to the facing (the line runs along it).
        public static void SpawnLine(Vector2 center, float rotation, int count, float spacing, BotSpawnSpec spec, BotAiEnum ai, BotDeathPolicy death)
        {
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
            Logger.Log($"SpawnLine: {count} bots, spacing {spacing:F2}, facing {rotation:F0} deg.", LogLevel.INFO);
        }

        // ---- Map-load staging ----

        // Cleared at the start of each config batch so a fresh PassConfigVariables fully rebuilds the set
        // (and re-passing the same config each round doesn't accumulate duplicate lines).
        public static void ClearStaged()
        {
            if (_staged.Count == 0) return;
            _staged.Clear();
            Logger.Log("Cleared staged spawn lines.", LogLevel.DEBUG);
        }

        public static void StageLine(StagedLine line)
        {
            _staged.Add(line);
            Logger.Log($"Staged spawn line: {line.Spec.Count}x {line.Spec.FactionToken}/{line.Spec.Class} at ({line.Center.x:F1}, {line.Center.y:F1}) facing {line.Rotation:F0} deg, AI {line.Spec.Ai}, death {line.Spec.Death}.", LogLevel.INFO);
        }

        // Called once per round (StateTracker.OnRoundDetails) to populate the staged lines for that round.
        public static void SpawnStagedLines()
        {
            if (_staged.Count == 0) return;

            if (!Application.isPlaying)
            {
                SpawnAllStaged(); // Edit-mode/tests: no coroutine host - spawn synchronously, no delay.
                return;
            }

            MonoBehaviourRunner.Instance.StartCoroutine(SpawnStagedAfterDelay());
        }

        public static void Reset()
        {
            _staged.Clear();
        }

        // ---- Internals ----

        private static IEnumerator SpawnStagedAfterDelay()
        {
            yield return new WaitForSeconds(SpawnDelaySeconds);
            SpawnAllStaged();
        }

        private static void SpawnAllStaged()
        {
            float spacing = ((LineSpacingConfigurable)ConfigurableRegistry.Get(ConfigurableEnum.LineSpacing)).LineSpacing;
            int spawned = 0;

            foreach (var line in _staged)
            {
                var s = line.Spec;
                if (!FactionTokens.TryResolve(s.FactionToken, out FactionCountry faction))
                {
                    Logger.Log($"Skipping staged line: cannot resolve faction '{s.FactionToken}'.", LogLevel.WARNING);
                    continue;
                }

                var spec = new BotSpawnSpec(faction, s.Class, s.Name, s.RegTag, s.UniformId);
                SpawnLine(line.Center, line.Rotation, s.Count, spacing, spec, s.Ai, s.Death);
                spawned++;
            }

            Logger.Log($"Spawned {spawned} staged line(s) for the new round.", LogLevel.INFO);
        }
    }
}
