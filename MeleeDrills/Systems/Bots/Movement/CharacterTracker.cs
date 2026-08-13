using System.Collections.Generic;
using UnityEngine;

namespace MDS.Systems
{
    // A once-per-tick snapshot of every spawned character, human players as well as bots, with planar position and
    // a smoothed velocity. Built once per bot tick and shared by all bots, so neighbour-aware steering (Separation,
    // Collision Avoidance) sees players too and we don't repeat an O(n) gather per bot.
    //
    // Velocities come from transform deltas. Player positions update at packet rate, slower than the tick, so
    // smoothing matters; and because we teleport bots on summon or line spawn, an implausibly large step is
    // treated as a teleport or respawn and reported as zero velocity rather than a huge spike.
    public static class CharacterTracker
    {
        public readonly struct Character
        {
            public readonly int PlayerId;
            public readonly Vector2 Position;   // world XZ
            public readonly Vector2 Velocity;   // world XZ units/sec

            public Character(int playerId, Vector2 position, Vector2 velocity)
            {
                PlayerId = playerId;
                Position = position;
                Velocity = velocity;
            }
        }

        private const float VelocitySmoothing = 0.3f;
        private const float MaxStepPerTick = 2f;   // metres; beyond this it's a teleport/respawn, not motion
        private const float MaxStepSqr = MaxStepPerTick * MaxStepPerTick;

        private static readonly List<Character> _characters = new();
        private static readonly Dictionary<int, Vector2> _lastPosition = new();
        private static readonly Dictionary<int, Vector2> _velocity = new();

        // The current tick's snapshot (valid for the tick in which Refresh was called).
        public static IReadOnlyList<Character> Characters => _characters;

        // Rebuilds the snapshot from the live transforms of every spawned player/bot.
        public static void Refresh(float deltaTime)
        {
            _characters.Clear();

            IReadOnlyList<IPlayer> players = StateTracker.AllPlayers;
            for (int i = 0; i < players.Count; i++)
            {
                IPlayer player = players[i];
                if (player?.PlayerObject == null) continue; // not currently spawned

                Vector3 world = player.PlayerObject.transform.position;
                Vector2 position = new Vector2(world.x, world.z);
                int id = player.PlayerId;

                Vector2 velocity = Vector2.zero;
                if (_lastPosition.TryGetValue(id, out Vector2 last) && deltaTime > 0f)
                {
                    Vector2 step = position - last;
                    if (step.sqrMagnitude <= MaxStepSqr) // ignore teleports / respawns
                    {
                        _velocity.TryGetValue(id, out Vector2 previous);
                        velocity = Vector2.Lerp(previous, step / deltaTime, VelocitySmoothing);
                    }
                }

                _lastPosition[id] = position;
                _velocity[id] = velocity;
                _characters.Add(new Character(id, position, velocity));
            }
        }

        public static bool TryGetVelocity(int playerId, out Vector2 velocity) =>
            _velocity.TryGetValue(playerId, out velocity);

        // Drops one player's tracked motion, called when they leave, so a recycled player id doesn't start with
        // the previous holder's position and velocity.
        public static void Clear(int playerId)
        {
            _lastPosition.Remove(playerId);
            _velocity.Remove(playerId);
        }

        // Cleared on round change so ids from the previous round don't linger.
        public static void Reset()
        {
            _characters.Clear();
            _lastPosition.Clear();
            _velocity.Clear();
        }
    }
}
