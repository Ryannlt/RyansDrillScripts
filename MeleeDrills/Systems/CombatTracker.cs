using System.Collections.Generic;
using UnityEngine;
using HoldfastSharedMethods;

namespace MDS.Systems
{
    // Turns the edge-triggered melee PlayerActions in each packet into a per-player melee state.
    public static class CombatTracker
    {
        // How long after a commit to keep the block up, covering the stab's lethal window.
        public const float LethalWindowSeconds = 1.0f;

        public struct MeleeState
        {
            public string WindupDir;   // "High"/"Low"/"Left"/"Right" of the latest windup (null if never seen)
            public float WindupTime;    // realtime of the latest windup start
            public float CommitTime;    // realtime of the latest ExecuteMeleeWeaponStrike
            public bool WindingUp;      // a windup is chambered: seen, not yet committed or canceled

            // True while this player is a melee threat: winding up, or a committed swing still in flight.
            public bool IsThreat(float now) => WindingUp || (now - CommitTime) < LethalWindowSeconds;
        }

        private static readonly Dictionary<int, MeleeState> _states = new();

        // Realtime of each player's most recent successful block.
        private static readonly Dictionary<int, float> _lastBlock = new();

        // Who each defender most recently blocked, the attacker of their last absorbed hit. Lets a bot engage the
        // player who actually attacked it, rather than anyone merely swinging nearby.
        private static readonly Dictionary<int, int> _lastBlockAttacker = new();

        public static void OnPacket(int playerId, PlayerActions[] actions)
        {
            if (actions == null || actions.Length == 0) return;

            _states.TryGetValue(playerId, out MeleeState s);
            float now = Time.realtimeSinceStartup;
            bool changed = false;

            for (int i = 0; i < actions.Length; i++)
            {
                string name = actions[i].ToString();

                if (name.Length > 11 && name.StartsWith("MeleeStrike"))       // MeleeStrike{High|Low|...}
                {
                    s.WindupDir = name.Substring(11);
                    s.WindupTime = now;
                    s.WindingUp = true;
                    changed = true;
                }
                else if (name == "ExecuteMeleeWeaponStrike")                  // committed the swing
                {
                    s.CommitTime = now;
                    s.WindingUp = false;
                    changed = true;
                }
                else if (name.StartsWith("MeleeBlock") || name == "StartMeleeBlock")
                {
                    // a block mid-windup is a feint or cancel; the chamber is gone.
                    if (s.WindingUp) { s.WindingUp = false; changed = true; }
                }
            }

            if (changed) _states[playerId] = s;
        }

        public static bool TryGet(int playerId, out MeleeState state) => _states.TryGetValue(playerId, out state);

        // A block landed: defenderId successfully blocked attackerId's strike. This does not shorten the attacker's
        // own recovery; a blocked stab still costs the full ~1.5s, the same as a miss.
        public static void OnBlock(int attackerId, int defenderId)
        {
            _lastBlock[defenderId] = Time.realtimeSinceStartup;
            _lastBlockAttacker[defenderId] = attackerId;
        }

        // Realtime of playerId's last successful block as defender, or 0 if none seen.
        public static float LastBlockTime(int playerId) => _lastBlock.TryGetValue(playerId, out float t) ? t : 0f;

        // The attacker of playerId's last absorbed hit (as defender), or null if none seen.
        public static int? LastBlockAttacker(int playerId) => _lastBlockAttacker.TryGetValue(playerId, out int a) ? a : (int?)null;

        // Drops one player's melee state, called when they leave.
        public static void Clear(int playerId)
        {
            _states.Remove(playerId);
            _lastBlock.Remove(playerId);
            _lastBlockAttacker.Remove(playerId);
        }

        public static void Reset()
        {
            _states.Clear();
            _lastBlock.Clear();
            _lastBlockAttacker.Clear();
        }
    }
}
