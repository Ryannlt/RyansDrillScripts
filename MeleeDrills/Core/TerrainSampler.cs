using UnityEngine;

namespace MDS.Core
{
    public static class TerrainSampler
    {
        // The probe ray starts this far above the highest terrain sample and travels this far down, so the
        // search window spans 20m above to 20m below that sample.
        private const float RayHeightAboveTerrain = 20f;
        private const float RayLength = 40f;

        // Surfaces the ground probe may land on. Deliberately EXCLUDES the Player layer, so a player or bot
        // standing on the spot can't be mistaken for ground (that would place a spawn on their shoulders).
        // Add names here if a map puts stand-on geometry elsewhere (e.g. "Ship"); a miss logs a warning.
        private static readonly string[] GroundLayerNames = { "Terrain", "Static Environment", "WalkablePlatform" };
        private static readonly int GroundMask = LayerMask.GetMask(GroundLayerNames);

        // Finds the ground height at a world X,Z.
        //
        // Maps can have MULTIPLE terrains, and Terrain.activeTerrain returns only one of them - which put
        // bots on the wrong (lower) terrain. So we sample EVERY active terrain and take the highest result,
        // not as the answer but to place a probe ray sensibly. A downward raycast from just above that height
        // is the real authority: it lands on whatever surface is actually there (terrain, bridge, platform,
        // static geometry) - restricted to GroundLayerNames, so it can never land on a player standing at
        // that spot. Only if the ray misses do we fall back to the sampled terrain height.
        public static float GetYAt(Vector2 position)
        {
            float terrainY = 0f;
            Terrain[] terrains = Terrain.activeTerrains;

            if (terrains != null && terrains.Length > 0)
            {
                float highestY = float.MinValue;

                foreach (Terrain terrain in terrains)
                {
                    if (terrain == null) continue;

                    float sampledY = terrain.SampleHeight(new Vector3(position.x, 0f, position.y)) + terrain.GetPosition().y;
                    if (sampledY > highestY)
                        highestY = sampledY;
                }

                if (highestY > float.MinValue)
                    terrainY = highestY; // guard: every entry was null, keep the 0 default
            }

            Vector3 origin = new Vector3(position.x, terrainY + RayHeightAboveTerrain, position.y);
            if (Physics.Raycast(origin, Vector3.down, out RaycastHit hitInfo, RayLength, GroundMask))
                return hitInfo.point.y;

            Logger.Log($"Could not find surface height at ({position.x}, {position.y}). Falling back to terrain height {terrainY:F2}.", LogLevel.WARNING);
            return terrainY;
        }
    }
}
