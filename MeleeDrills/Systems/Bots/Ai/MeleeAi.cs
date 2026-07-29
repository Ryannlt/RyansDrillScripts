using UnityEngine;

namespace MDS.Systems
{
    // A melee combat AI. Faces the nearest enemy, holds melee spacing, and reactively BLOCKS its attacks with
    // the guard that counters the enemy's windup direction (see BlockTokenFor). Perception comes from
    // CombatTracker (the enemy's melee state, read from its packets); actuation is the BotIntent action
    // channel (block tokens confirmed on a live bot via 'rc bot act').
    //
    // Two modes: Defend blocks only (a test/target for practising attacks against); Fight also ATTACKS - it
    // crowds in and throws stabs whenever the enemy isn't threatening, which naturally becomes a riposte right
    // after a block (the enemy is then in recovery - "stab priority"). A raw MeleeStrike token latches an auto-
    // cycling attack loop, so a strike is a SHORT held chamber (MeleeStrike re-sent a few ticks) released by a
    // single ExecuteMeleeWeaponStrike (which also stops the cycle). See StepAttack.
    public class MeleeAi : IBotAi
    {
        public enum Mode { Defend, Fight }

        // Melee spacing. Two distances, re-rolled with a little jitter (RollHoldRange): an OFFENSIVE range to
        // press/attack from (close), and a DEFENSIVE range to sit at while guarding/reading (further). Fight uses
        // offensive when it's free and defensive when it's guarding; Defend (pure defender) always uses defensive.
        // RangeTolerance is the slop band around the target distance where the bot just stands (no micro-shuffling).
        private const float FightRange = 0.7f;        // Fight offensive base (~0.7-0.8 with jitter)
        private const float FightRangeVariance = 0.1f;
        private const float DefendRange = 2.3f;       // defensive base (~2.3-2.7 ≈ 2.5 reading distance)
        private const float DefendRangeVariance = 0.4f;
        private const float RangeTolerance = 0.3f;
        private const float BackoffThrottle = 1.0f;   // back off at FULL speed (like the avoid behavior) so an approaching attacker can't just fill the gap
        // Movement-strategy delay: adopting a CLOSER hold range (starting to advance) waits a short reaction beat,
        // like a real player who was backing up not instantly sprinting in. Retreating stays immediate.
        private const float MoveChangeDelay = 0.2f;
        private const float MoveHysteresis = 0.5f;    // ignore range jitter smaller than this when deciding "advance"

        // Fight-mode attack tuning. The chamber is kept SHORT: a raw MeleeStrike latches a strike the engine
        // auto-fires after ~0.5s, so we hold the chamber only a few ticks then release before it self-triggers.
        private const float WindupSeconds = 0.15f;    // hold the windup this long (ONE MeleeStrike) before releasing
        // A committed stab occupies the bot ~this long before it can re-throw - whether it MISSES or is BLOCKED
        // (both measured at ~1.5-1.6s from a human spamming attack: the swing+recovery must play out, and the
        // engine's ~0.35s block stun does NOT shortcut it). We must NOT queue the next strike before this elapses
        // or the command overlaps the still-playing swing (the "extra queued attack" glitch: restart, no pause).
        // Timed from release (Execute). Ripostes bypass this via priority.
        private const float MissedStabDuration = 1.5f;
        private const float ReadBeatMax = 0.6f;       // + a short randomized read/decision beat so pressure isn't robotic
        private const float AttackRange = 1.7f;       // within this distance a stab can land, so we throw
        private const float PriorityWindow = 0.6f;    // window to riposte, running AFTER the reaction beat (wide enough to cover the reaction + advance delay + close-in)
        private const float RiposteReactionMin = 0.0f; // human reaction beat between a block landing and the counter
        private const float RiposteReactionMax = 0.1f;
        private const float StrikeCommitWindow = 0.5f;// after releasing a strike, don't block or we cancel our own swing before it lands
        private const float FirstStrikeCommitBonus = 0.4f; // extra commit time when WE threw first - back our own stab instead of flinching into a guard
        private const float MinBlockHold = 0.35f;     // keep a raised guard up at least this long so it reads/animates

        // Stab aim: the bot faces the target's ACTUAL position (leading it made the bot over-rotate up close).
        // The stab comes off the RIGHT of the body, so while striking we shift the aim sideways by this much to
        // re-centre the thrust on centre of mass. Positive = aim to the bot's LEFT (compensating a right-hand
        // stab); flip the sign if it biases the other way. An in-stab sweep to catch movers is a later tuning pass.
        private const float StabAimOffset = 0.3f;     // metres

        private readonly Mode _mode;
        private float _offensiveRange;                // close spacing to press/attack from; re-rolled for jitter
        private float _defensiveRange;                // further spacing while guarding/reading; re-rolled for jitter
        private float _appliedRange;                  // the hold range actually driving movement (lags on advancing)
        private float _advanceWantedSince = -1f;      // realtime we first wanted to advance to a closer range (-1 = not)

        private int? _targetId;               // sticky target; re-acquired to nearest enemy when it drops
        private bool _stancePending = true;   // issue EnableCombatStance once, on the first spawned tick
        private bool _runPending = true;      // establish the sticky run toggle once, on first engagement
        private string _blockToken;           // the block playerAction we're currently holding (null = not blocking)

        // Fight-mode attack sequencer state (see StepAttack).
        private enum AttackPhase { None, Chamber }
        private AttackPhase _attackPhase;
        private string _attackDir;            // "High"/"Low" for the strike in progress
        private float _attackCooldownUntil;   // realtime before which we won't start another strike
        private float _chamberStartedAt;      // realtime our windup began (for the "I threw first" read)
        private float _executeAt;             // realtime to release the held windup
        private bool _threwFirst;             // this swing out-timed the enemy's - commit to it harder

        // Stab-priority state: after our guard absorbs a committed attack we get a brief riposte window (Decide).
        private float _lastConsumedBlock;     // last OnPlayerBlock (we blocked THEM) reacted to (dedupe)
        private float _riposteReadyAt;        // realtime before which we hold the guard (reaction beat) before countering
        private float _priorityUntil;         // while now < this: riposte immediately, don't re-block
        private float _strikeCommittedUntil;  // while now < this: our own strike is in flight - don't block (it'd cancel it)
        private float _blockStartedAt;        // realtime the current guard went up (for MinBlockHold)
        private float _rerollAt;              // realtime to next re-roll the hold distance

        public MeleeAi(Mode mode)
        {
            _mode = mode;
            RollHoldRange();
        }

        public BotAiEnum AiType => _mode == Mode.Fight ? BotAiEnum.MeleeFight : BotAiEnum.MeleeDefend;

        public BotIntent Decide(BotController self, float deltaTime)
        {
            if (!self.TryGetPose(out BotPose pose))
                return BotIntent.Idle; // not spawned - issue nothing

            // One-time: enter combat stance so the bot can block/strike. Consumed only once actually spawned.
            if (_stancePending)
            {
                _stancePending = false;
                return new BotIntent { Action = "EnableCombatStance" };
            }

            IPlayer target = ResolveTarget(self);
            if (target?.PlayerObject == null)
            {
                _attackPhase = AttackPhase.None; // don't resume a stale chamber when a target is reacquired
                return DropBlock(new BotIntent { MoveAxis = Vector2.zero }); // no enemy: stand, lower guard
            }

            Vector3 tp = target.PlayerObject.transform.position;
            Vector2 targetPos = new Vector2(tp.x, tp.z);
            CombatTracker.TryGet(target.PlayerId, out CombatTracker.MeleeState enemy);
            float now = Time.realtimeSinceStartup;

            // Face the target's ACTUAL position - leading where it's going made the bot over-rotate/spin up
            // close. While striking, nudge the aim sideways to compensate for the stab coming off the RIGHT of
            // the body, so the thrust lands on centre of mass. A moving target is caught a little behind for now
            // (a controlled in-stab sweep is a later tuning pass), which is fine.
            Vector2 aimPos = targetPos;
            if (_attackPhase == AttackPhase.Chamber || now < _strikeCommittedUntil)
            {
                Vector2 toTarget = targetPos - pose.Position;
                if (toTarget.sqrMagnitude > 1e-4f)
                {
                    Vector2 dir = toTarget.normalized;
                    Vector2 botLeft = new Vector2(-dir.y, dir.x); // bot's left; shift aim here to centre a right-hand stab
                    aimPos = targetPos + botLeft * StabAimOffset;
                }
            }
            BotIntent intent = new BotIntent { LookHeading = MovementSolver.HeadingTo(pose.Position, aimPos) };

            // Stab priority: after our guard ABSORBS the enemy's committed stab they're recovering and can't beat
            // our counter, so we hand ourselves a brief window to riposte at once (ignoring the attack cooldown).
            // Priority comes ONLY from the engine's block event - a real absorbed hit, never a guess.
            bool priority = false;
            if (_mode == Mode.Fight)
            {
                float myBlock = CombatTracker.LastBlockTime(self.PlayerId);
                if (myBlock > _lastConsumedBlock)
                {
                    _lastConsumedBlock = myBlock;
                    _riposteReadyAt = now + Random.Range(RiposteReactionMin, RiposteReactionMax); // reaction beat before we counter
                    _priorityUntil = _riposteReadyAt + PriorityWindow;                             // counter window runs AFTER the beat
                }

                // But only actually riposte if the enemy has NOT readied a fresh stab since that block: not
                // winding one up now, and no windup newer than the block we absorbed. Otherwise they're holding a
                // chambered stab (or just released one our guard hasn't caught yet) and would spear us the instant
                // the guard drops - the feint-then-hold exploit. Keep blocking; we re-earn priority when our
                // guard absorbs THAT stab too. (This also retires the old lethal-window timer, which fired on a
                // mere commit and was exactly what the exploit baited.)
                priority = now >= _riposteReadyAt && now < _priorityUntil && !enemy.WindingUp && enemy.WindupTime <= _lastConsumedBlock;
            }

            // Vary the spacing over time so the bot isn't pinned to one radius (feels more like a real duellist).
            if (now >= _rerollAt) { RollHoldRange(); _rerollAt = now + Random.Range(1.5f, 3.5f); }

            // While our OWN strike is still flying, we must not block - a block cancels the swing before it lands
            // (the same feint mechanic, used against ourselves). Commit to it.
            bool committed = now < _strikeCommittedUntil;

            // "I threw first" read: our windup began before the enemy started theirs, so our stab lands first -
            // commit it (even if they've already committed a reaction throw) rather than bailing to a block.
            // Using IsThreat (not just WindingUp) so an instant reaction-throw still counts as "they went second".
            bool chamberCommit = _mode == Mode.Fight && _attackPhase == AttackPhase.Chamber
                                 && enemy.IsThreat(now) && _chamberStartedAt <= enemy.WindupTime;
            if (chamberCommit) _threwFirst = true; // we out-timed them - commit harder to this swing (see StepAttack)

            string desiredBlock = (priority || committed || chamberCommit) ? null : DesiredBlockToken(enemy, now);

            // Minimum block hold: once the guard is up, keep it up briefly even if we'd now drop it (to riposte),
            // so it reads and its animation completes. Never overrides committing our own in-flight strike.
            if (desiredBlock == null && _blockToken != null && !committed && !chamberCommit
                && now - _blockStartedAt < MinBlockHold)
                desiredBlock = _blockToken;

            if (desiredBlock != null)
            {
                // Under threat: block. Abort any in-progress strike - raising a block cancels our own windup.
                _attackPhase = AttackPhase.None;
                if (_blockToken != desiredBlock)
                {
                    if (_blockToken == null) _blockStartedAt = now; // this guard just went up
                    intent.Action = desiredBlock;                  // start, or switch direction (no StopMeleeBlock needed)
                    _blockToken = desiredBlock;
                }
                // Guarding: hold the further DEFENSIVE distance to make space to read, following a circling
                // player instead of freezing.
                intent.MoveAxis = HoldRange(pose, targetPos, MovementRange(false, now));
            }
            else
            {
                // Not threatened (or riposting with priority): lower the guard and keep melee spacing.
                bool droppedBlock = false;
                if (_blockToken != null)
                {
                    intent.Action = "StopMeleeBlock";
                    _blockToken = null;
                    droppedBlock = true;
                }
                // Free: Fight closes to its OFFENSIVE range to press; Defend just holds its reading distance.
                intent.MoveAxis = HoldRange(pose, targetPos, MovementRange(_mode == Mode.Fight, now));

                // Fight mode attacks when the enemy isn't threatening. With 'priority' this is the post-block
                // riposte and fires immediately (ignoring the cooldown); otherwise strikes are cooldown-paced.
                // Skip the tick we drop the block (StopMeleeBlock took the action channel; strike resumes next).
                if (_mode == Mode.Fight && !droppedBlock)
                    StepAttack(ref intent, pose, targetPos, priority);
            }

            // Establish run once, on the first tick we actually engage a target (sticky engine toggle).
            if (_runPending)
            {
                intent.Running = true;
                _runPending = false;
            }

            return intent;
        }

        // The block playerAction to hold this tick, or null to lower the guard. We block whenever the enemy is
        // a melee threat - winding up, or a committed swing still in its lethal window.
        private static string DesiredBlockToken(CombatTracker.MeleeState enemy, float now)
        {
            if (string.IsNullOrEmpty(enemy.WindupDir)) return null;
            if (!enemy.IsThreat(now)) return null;
            return BlockTokenFor(enemy.WindupDir);
        }

        // Maps the enemy's attack direction (in THEIR frame) to the block the bot raises (in ITS frame).
        // High/Low are overhead/underhand - shared, so they match directly. Left/Right are MIRRORED: the
        // duellists face each other, so the attacker's right side is the defender's left, and vice versa.
        private static string BlockTokenFor(string windupDir)
        {
            switch (windupDir)
            {
                case "Left":  return "MeleeBlockRight";
                case "Right": return "MeleeBlockLeft";
                default:      return "MeleeBlock" + windupDir; // High / Low
            }
        }

        // Fight-mode attack sequencer. A strike is ONE MeleeStrike{dir} (starts + holds the windup), silence
        // while it holds, then ONE ExecuteMeleeWeaponStrike to release it. One strike, then a cooldown. Blocking
        // pre-empts this (handled in Decide). 'priority' (a post-block riposte) bypasses the cooldown.
        private void StepAttack(ref BotIntent intent, BotPose pose, Vector2 targetPos, bool priority)
        {
            float now = Time.realtimeSinceStartup;

            if (_attackPhase == AttackPhase.Chamber)
            {
                // The windup is held by the SINGLE MeleeStrike already sent - re-sending it every tick restarts
                // the windup animation (the glitch / "invisible" stab), so while holding we issue NOTHING. One
                // ExecuteMeleeWeaponStrike releases it and ends the swing cleanly (so it can't then auto-cycle).
                if (now >= _executeAt)
                {
                    intent.Action = "ExecuteMeleeWeaponStrike";
                    _attackPhase = AttackPhase.None;
                    _attackCooldownUntil = now + MissedStabDuration + Random.Range(0f, ReadBeatMax);
                    _priorityUntil = 0f;                              // riposte thrown - priority spent
                    // Commit to the swing (blocking now would cancel it). If we THREW FIRST, commit LONGER so the
                    // bot backs its own stab as it lands instead of flinching into a guard and eating the trade.
                    _strikeCommittedUntil = now + StrikeCommitWindow + (_threwFirst ? FirstStrikeCommitBonus : 0f);
                }
                return;
            }

            // Idle: begin a strike if close enough. A priority riposte ignores the cooldown (fires immediately);
            // otherwise strikes are paced by it.
            if (!priority && now < _attackCooldownUntil) return;
            if ((targetPos - pose.Position).sqrMagnitude > AttackRange * AttackRange) return;

            // Start the windup with ONE MeleeStrike; we then hold with silence and release on the timer above.
            _attackDir = Random.value < 0.5f ? "High" : "Low"; // bayonet is High/Low; unblockable during recovery anyway
            _chamberStartedAt = now;
            _executeAt = now + WindupSeconds;
            _threwFirst = false;                               // set true only if we out-time the enemy this windup
            _attackPhase = AttackPhase.Chamber;
            intent.Action = "MeleeStrike" + _attackDir;
        }

        // Re-rolls the offensive and defensive spacings with a little jitter, so the bot varies its distance
        // over time instead of orbiting at a fixed radius.
        private void RollHoldRange()
        {
            _offensiveRange = FightRange + Random.Range(0f, FightRangeVariance);
            _defensiveRange = DefendRange + Random.Range(0f, DefendRangeVariance);
        }

        // The hold range to actually move toward this tick. Switching to a CLOSER range (advancing) is held off
        // for a short reaction beat - a real player backing up takes a moment before running in. Backing off (a
        // larger range) applies immediately. Jitter below MoveHysteresis doesn't count as a strategy change.
        private float MovementRange(bool wantOffensive, float now)
        {
            float target = wantOffensive ? _offensiveRange : _defensiveRange;
            if (target < _appliedRange - MoveHysteresis)      // wants to advance (move closer)
            {
                if (_advanceWantedSince < 0f) _advanceWantedSince = now;
                if (now - _advanceWantedSince >= MoveChangeDelay) _appliedRange = target;
            }
            else                                              // backing off or holding: no delay
            {
                _appliedRange = target;
                _advanceWantedSince = -1f;
            }
            return _appliedRange;
        }

        // Local input axis to sit at the given hold range: close in if too far, ease back if too close, stand inside
        // the tolerance band. Re-solved each tick against the live pose since the target moves.
        private static Vector2 HoldRange(BotPose pose, Vector2 targetPos, float range)
        {
            Vector2 toTarget = targetPos - pose.Position;
            float dist = toTarget.magnitude;
            if (dist < 1e-4f) return Vector2.zero;

            Vector2 dir = toTarget / dist;
            if (dist > range + RangeTolerance) return MovementSolver.ToLocalAxis(pose, dir, 1f);
            if (dist < range - RangeTolerance) return MovementSolver.ToLocalAxis(pose, -dir, BackoffThrottle);
            return Vector2.zero;
        }

        // Sticky nearest-enemy targeting: keep the current target while it's a valid live enemy, otherwise
        // acquire the nearest one. (An assignable/pinned target for drills layers on top of this later.)
        private IPlayer ResolveTarget(BotController self)
        {
            if (_targetId.HasValue)
            {
                IPlayer current = StateTracker.GetPlayerById(_targetId.Value);
                if (IsEnemy(self, current)) return current;
            }

            IPlayer nearest = FindNearestEnemy(self);
            _targetId = nearest?.PlayerId;
            return nearest;
        }

        private static IPlayer FindNearestEnemy(BotController self)
        {
            if (!(self.Position is Vector3 selfPos)) return null;

            IPlayer nearest = null;
            float bestSqr = float.MaxValue;

            var players = StateTracker.AllPlayers;
            for (int i = 0; i < players.Count; i++)
            {
                IPlayer p = players[i];
                if (!IsEnemy(self, p)) continue;

                float sqr = (p.PlayerObject.transform.position - selfPos).sqrMagnitude;
                if (sqr < bestSqr) { bestSqr = sqr; nearest = p; }
            }

            return nearest;
        }

        // An enemy is a spawned player on a different, valid faction (not us, not a teammate, not a spectator).
        private static bool IsEnemy(BotController self, IPlayer p)
        {
            return p != null
                && p.PlayerId != self.PlayerId
                && p.PlayerObject != null
                && p.Faction.HasValue
                && self.Bot.Faction.HasValue
                && p.Faction.Value != self.Bot.Faction.Value;
        }

        private BotIntent DropBlock(BotIntent intent)
        {
            if (_blockToken != null)
            {
                intent.Action = "StopMeleeBlock";
                _blockToken = null;
            }
            return intent;
        }

        // Melee auto-acquires its target and re-enters stance on spawn, so a Replace replacement (a fresh
        // instance of the same AiType, hence the same mode) needs nothing carried over.
        public void InheritFrom(IBotAi previous) { }
    }
}
