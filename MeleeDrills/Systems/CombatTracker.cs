using System.Collections.Generic;
using UnityEngine;
using HoldfastSharedMethods;

namespace MDS.Systems
{
    // Turns the EDGE-triggered melee PlayerActions in each player's packets into a durable, queryable melee
    // state, so a bot can react to an enemy's attack. Fed from OnPlayerPacket (like MeleeProbe / CharacterTracker).
    //
    // Confirmed vocabulary (probe): a primary attack winds up as MeleeStrike{High|Low} (direction in the
    // token) and commits with ExecuteMeleeWeaponStrike; a block is MeleeBlock{High|Low|Left|Right}. A block
    // appearing DURING a windup is a feint/cancel - there is always a block between two attack holds.
    public static class CombatTracker
    {
        // How long after a commit to keep the block up. The stab has a LETHAL PHASE with real duration, and
        // the tip can connect anywhere in it: point-blank it lands early (~0.3s), but at melee range - or when
        // the attacker deliberately stabs wide and TURNS the tip into the defender - contact lands LATE (0.76s
        // seen killing the bot through a 0.55s hold). So block through the whole window. This is the primary
        // defensive knob: raise it if a delayed/turned stab still sneaks through - a longer guard costs nothing
        // in Defend mode (a fresh windup in a new direction still re-aims the block immediately regardless).
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

        // Realtime of each player's most recent successful block (as the DEFENDER). The engine's OnPlayerBlock
        // fires the instant a block absorbs a hit, so this is a precise "the swing is spent, you're clear to
        // counter" signal - far better than timing the lethal window blind.
        private static readonly Dictionary<int, float> _lastBlock = new();

        // Who each defender most recently blocked (the attacker of their last absorbed hit). Lets a bot engage
        // the player who actually attacked IT, rather than anyone merely swinging nearby.
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
                    // a block mid-windup is a feint (cancel) - the chamber is gone.
                    if (s.WindingUp) { s.WindingUp = false; changed = true; }
                }
            }

            if (changed) _states[playerId] = s;
        }

        public static bool TryGet(int playerId, out MeleeState state) => _states.TryGetValue(playerId, out state);

        // A block landed: defenderId successfully blocked attackerId's strike. (The attacker's own recovery is
        // NOT shortcut by this - a blocked stab still costs the full ~1.5s swing recovery, same as a miss.)
        public static void OnBlock(int attackerId, int defenderId)
        {
            _lastBlock[defenderId] = Time.realtimeSinceStartup;
            _lastBlockAttacker[defenderId] = attackerId;
        }

        // Realtime of playerId's last successful block as defender, or 0 if none seen.
        public static float LastBlockTime(int playerId) => _lastBlock.TryGetValue(playerId, out float t) ? t : 0f;

        // The attacker of playerId's last absorbed hit (as defender), or null if none seen.
        public static int? LastBlockAttacker(int playerId) => _lastBlockAttacker.TryGetValue(playerId, out int a) ? a : (int?)null;

        public static void Reset()
        {
            _states.Clear();
            _lastBlock.Clear();
            _lastBlockAttacker.Clear();
        }
    }
}
