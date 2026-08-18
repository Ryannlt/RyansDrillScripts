using System.Collections.Generic;
using UnityEngine;
using HoldfastSharedMethods;

namespace MDS.Systems
{
    // Dev and instrumentation tool with no gameplay effect. Logs a chosen player's melee-relevant packet signals
    // so we could learn the exact PlayerActions vocabulary and timings before coding the block logic: which tokens
    // mean attack high/low/left/right versus block, the lead time from a windup to the hit, and the recovery
    // window after. Toggle it with 'rc bot probe <id|me>'.
    //
    // To keep the log readable it prints a player's actions only when the set changes, with a monotonic timestamp
    // and the delta since the last change, so the stream reads as a clean windup, release, hurt, recovery
    // timeline. Probe both fighters at once (it holds a set) to interleave attacker and victim. OnPacket fires for
    // every player every packet, so it fast-exits when nothing is being probed.
    public static class MeleeProbe
    {
        private static readonly HashSet<int> _probed = new();
        private static readonly Dictionary<int, string> _lastActions = new();
        private static readonly Dictionary<int, float> _lastChangeTime = new();

        public static bool IsProbing(int playerId) => _probed.Contains(playerId);

        // The last swing a bot released, kept for every bot rather than only probed ones. Friendly-fire kills are
        // rare and are the thing being hunted, so having to remember to enable probing first is how the evidence
        // gets missed.
        private struct StrikeRecord
        {
            public float ReleasedAt;
            public float AimDesired;   // heading the swing wanted
            public float AimClamped;   // heading it was allowed after clearing squadmates
            public int TargetId;
            public bool LaneClear;     // what the start gate thought when the swing began
        }

        private static readonly Dictionary<int, StrikeRecord> _lastStrike = new();

        public static void NoteStrike(int playerId, float now, float aimDesired, float aimClamped, int targetId, bool laneClear)
        {
            _lastStrike[playerId] = new StrikeRecord
            {
                ReleasedAt = now,
                AimDesired = aimDesired,
                AimClamped = aimClamped,
                TargetId = targetId,
                LaneClear = laneClear,
            };
        }

        // One line per tick while a swing is live. The FriendlyFire line records the aim clamp only at release,
        // which says nothing about what the clamp did during the swing, and during the swing is the only time it
        // does anything. Reading these in order across the ticks before a team kill answers the two questions
        // that geometry alone cannot: did the clamp engage, and did the bot's actual heading obey it.
        //
        // actual is where the blade is pointing now, clamped is where we just told it to point. The two drifting
        // apart is the bot failing to turn as instructed, which no change to the clamp's maths would fix.
        // turned is how far the commanded heading actually moves this tick, after the mate clamp. It is the
        // column that shows a sweep: the game joins the blade's position between its own frames with rays, so a
        // single large turn passes through everyone in between even when neither end of it was pointed at
        // anybody.
        // gateR and clampR are the lever values actually in force on this bot, not the configured defaults. A
        // whole test session was spent reading a bot as broken when the truth was that 'rc set globalAI' only
        // seeds bots spawned afterwards, so the running bot still had its old values. Printing what it is using
        // makes that self-evident on the first line instead of after the session.
        public static void LogSwingTick(int playerId, float actual, float desired, float clamped, float turned,
                                        bool mateAcross, float gateR, float clampR, string mates)
        {
            Logger.Log(
                $"MeleeSwing[{playerId}] actual={actual:0.#} desired={desired:0.#} clamped={clamped:0.#} " +
                $"held={Mathf.DeltaAngle(desired, clamped):0.#} turned={turned:0.#} across={mateAcross} " +
                $"gateR={gateR:0.##} clampR={clampR:0.##}{mates}",
                LogLevel.INFO);
        }

        // One line per friendly-fire kill, in the killer's own aim frame. The fields are chosen to separate the
        // candidate causes rather than just record that it happened:
        //
        //   behind=True, or a small negative along  -> the game's shaft cast, which runs from 0.25m behind the
        //                                              attacker out to the weapon origin and which we do not model
        //   small positive lateral                  -> the weapon origin sitting right of the body
        //   moved=0 every time                      -> the aim clamp never engaged, so the bug is in the clamp
        //   large lateral and a kill anyway         -> the tip OverlapSphere, so the radius is simply too small
        public static void LogFriendlyFire(int killerId, int victimId, Vector2 killerPos, float killerHeading, Vector2 victimPos)
        {
            Vector2 toVictim = victimPos - killerPos;
            float dist = toVictim.magnitude;

            Vector2 forward = MovementSolver.DirectionFromHeading(killerHeading);
            Vector2 right = new Vector2(forward.y, -forward.x);

            float along = Vector2.Dot(toVictim, forward);
            float lateral = Vector2.Dot(toVictim, right);
            float bearing = Mathf.DeltaAngle(killerHeading, MovementSolver.HeadingOf(toVictim));

            string strike = "no strike on record";
            if (_lastStrike.TryGetValue(killerId, out StrikeRecord r))
            {
                // bearing above is measured against the killer's heading NOW, which is well after the swing began
                // and after it has kept turning to track a moving target. bearingAtRelease measures the same
                // victim against where the bot was actually pointing when it committed, which is the frame the
                // swing was launched in. The two disagreeing means the bot turned into its mate after committing,
                // which is a different problem from the mate having been in the arc all along.
                float bearingAtRelease = Mathf.DeltaAngle(r.AimClamped, MovementSolver.HeadingOf(toVictim));

                strike = string.Format("strikeAge={0:0.00} bearingAtRelease={1:0.#} desired={2:0.#} clamped={3:0.#} moved={4:0.#} target={5} laneClear={6}",
                    Time.realtimeSinceStartup - r.ReleasedAt,
                    bearingAtRelease,
                    r.AimDesired,
                    r.AimClamped,
                    Mathf.DeltaAngle(r.AimDesired, r.AimClamped),
                    r.TargetId,
                    r.LaneClear);
            }

            Logger.Log(
                string.Format("FriendlyFire: {0} killed {1} | dist={2:0.00} bearing={3:0.#} along={4:0.00} lateral={5:0.00} behind={6} | {7}",
                    killerId, victimId, dist, bearing, along, lateral, along < 0f, strike),
                LogLevel.WARNING);
        }

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
                {
                    // The weapon's real strike properties are not available here. They are serialised per weapon
                    // class and only readable off the live weapon, which needs Assembly-CSharp. Run the
                    // MeleeLogger mod alongside this one for those, and read the two logs side by side.
                    Logger.Log($"MeleeProbe: probing player {playerId} - perform attacks/blocks/feints and read this log.", LogLevel.INFO);
                }
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

        // Called for every hurt event; logs when a probed player takes damage, to measure hit timing and blocks.
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
