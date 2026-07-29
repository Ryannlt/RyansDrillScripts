using System.Collections.Generic;
using UnityEngine;
using HoldfastSharedMethods;

namespace MDS.Systems
{
    // Dev / instrumentation tool (no gameplay effect). Logs a chosen player's melee-relevant packet signals
    // so we can learn the exact PlayerActions vocabulary and timings BEFORE coding the block FSM - which
    // tokens mean attack high/low/left/right vs. block, and the lead time from a windup to the hit and the
    // recovery window after. Toggle with 'rc bot probe <id|me>'.
    //
    // To keep the log readable it prints a player's actions ONLY when the set changes (with a monotonic
    // timestamp + delta since the last change), so the stream reads as a clean windup -> release -> hurt ->
    // recovery timeline. Probe BOTH fighters at once (it holds a set) to interleave attacker + victim.
    // OnPacket fires for every player every packet, so it fast-exits when nothing is being probed.
    public static class MeleeProbe
    {
        private static readonly HashSet<int> _probed = new();
        private static readonly Dictionary<int, string> _lastActions = new();
        private static readonly Dictionary<int, float> _lastChangeTime = new();

        // Returns the new on/off state.
        public static bool Toggle(int playerId)
        {
            bool on = !_probed.Contains(playerId);
            Set(playerId, on);
            return on;
        }

        public static void Set(int playerId, bool on)
        {
            if (on)
            {
                if (_probed.Add(playerId))
                    Logger.Log($"MeleeProbe: probing player {playerId} - perform attacks/blocks/feints and read this log.", LogLevel.INFO);
            }
            else if (_probed.Remove(playerId))
            {
                _lastActions.Remove(playerId);
                _lastChangeTime.Remove(playerId);
                Logger.Log($"MeleeProbe: stopped probing player {playerId}.", LogLevel.INFO);
            }
        }

        // Called for every player's packet; logs only probed players, only on an action-set change.
        public static void OnPacket(int playerId, PlayerActions[] actions, float? rotationY, float? yaw)
        {
            if (_probed.Count == 0 || !_probed.Contains(playerId)) return;

            string current = (actions == null || actions.Length == 0) ? "(none)" : string.Join(",", actions);
            if (_lastActions.TryGetValue(playerId, out string prev) && prev == current) return; // unchanged

            float now = Time.realtimeSinceStartup;
            float delta = _lastChangeTime.TryGetValue(playerId, out float last) ? now - last : 0f;
            _lastActions[playerId] = current;
            _lastChangeTime[playerId] = now;

            Logger.Log($"MeleeProbe[{playerId}] t={now:F2} (+{delta:F2}s) actions=[{current}] rotY={Fmt(rotationY)} yaw={Fmt(yaw)}", LogLevel.INFO);
        }

        // Called for every hurt event; logs when a PROBED player takes damage (measures hit timing / blocks).
        public static void OnHurt(int victimId, byte oldHp, byte newHp)
        {
            if (_probed.Count == 0 || !_probed.Contains(victimId)) return;

            Logger.Log($"MeleeProbe[{victimId}] t={Time.realtimeSinceStartup:F2} HURT {oldHp}->{newHp}{(newHp == 0 ? " (DEAD)" : "")}", LogLevel.INFO);
        }

        // Cleared on round change (player ids are round-scoped).
        public static void Reset()
        {
            _probed.Clear();
            _lastActions.Clear();
            _lastChangeTime.Clear();
        }

        private static string Fmt(float? v) => v.HasValue ? v.Value.ToString("F0") : "-";
    }
}
