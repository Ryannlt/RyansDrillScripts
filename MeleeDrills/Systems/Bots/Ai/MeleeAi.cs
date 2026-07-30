using System.Collections.Generic;
using UnityEngine;
using MDS.ConfigVariables;

namespace MDS.Systems
{
    // A melee combat AI. Faces its target, holds melee spacing, reactively BLOCKS the target's attacks with the
    // guard that counters its windup direction (see BlockTokenFor), and - depending on its capability toggles -
    // presses in to attack and/or ripostes after absorbing a hit. Perception comes from CombatTracker (the
    // enemy's melee state, read from its packets); actuation is the BotIntent action channel (block/strike
    // tokens confirmed on a live bot via 'rc bot act').
    //
    // ONE class, three presets (BotAiEnum): the old Defend/Fight "modes" are now three capability toggles -
    //   press   (throw the first blow when the enemy isn't threatening),
    //   riposte (counter after our guard absorbs a committed attack), and
    //   move    (hold/adjust melee spacing vs. stand our ground) -
    // bundled into named defaults by DefaultLeversFor:
    //   MeleeDefend = block only (press off, riposte off, move on, pursue off) - a target to practise attacks
    //                 against that holds ground and blocks but won't chase, so you can back off to disengage.
    //   MeleeFight  = press on, riposte on, move on - crowds in and trades.
    //   Sparring    = press off, riposte on, move off - stands its ground, blocks, and only counters once
    //                 provoked ("wait for someone to initiate"), engaging only the closest player in range.
    //   Guardian    = passive until attacked (engageOnAttack): reads/blocks the nearest player in range, then on
    //                 being struck locks onto that attacker and fights (full MeleeFight) until it or the attacker
    //                 dies, then returns to passive. A Replace replacement starts passive again.
    // Every tuning value is a lever (IConfigurableAi): 'rc bot cfg <id> <lever> <value>' per bot, defaulting
    // from globalAI. The strike MECHANIC constants (windup/recovery/commit timings) stay fixed - they encode how
    // the engine plays a stab out (measured), not difficulty, so exposing them would just let someone break it.
    //
    // Targeting is lever-driven too (see ResolveTarget): targetRange gates who it engages, ignoreTeam decides
    // whether it cares about faction, and stickyTarget picks "hold one foe" vs "closest each tick". On top of
    // that, an automatic attacker-lock keeps it on whoever is mid-strike through the exchange. An ITargetableAi
    // pin lets a future supervisor override the choice entirely.
    //
    // A raw MeleeStrike token latches an auto-cycling attack loop, so a strike is a SHORT held chamber (ONE
    // MeleeStrike) released by a single ExecuteMeleeWeaponStrike (which also stops the cycle). See StepAttack.
    public class MeleeAi : IBotAi, IConfigurableAi, ITargetableAi
    {
        // ---- Fixed strike mechanic (engine-measured invariants; NOT levers) ----
        private const float WindupSeconds = 0.15f;    // hold the windup this long (ONE MeleeStrike) before releasing
        // A committed stab occupies the bot ~this long before it can re-throw - whether it MISSES or is BLOCKED
        // (both measured at ~1.5-1.6s from a human spamming attack: the swing+recovery must play out, and the
        // engine's ~0.35s block stun does NOT shortcut it). We must NOT queue the next strike before this elapses
        // or the command overlaps the still-playing swing (the "extra queued attack" glitch). Timed from release.
        private const float MissedStabDuration = 1.5f;
        private const float StrikeCommitWindow = 0.5f; // after releasing a strike, don't block or we cancel our own swing before it lands
        private const float FirstStrikeCommitBonus = 0.4f; // extra commit time when WE threw first - back our own stab instead of flinching into a guard
        private const float MinBlockHold = 0.35f;      // keep a raised guard up at least this long so it reads/animates
        private const float AimOffset = 0.3f;          // sideways aim shift while striking to re-centre a right-hand stab (metres); fixed - no practical reason to tune

        // ---- Fixed movement feel (advanced; kept const for now, promote to levers later) ----
        private const float RangeTolerance = 0.3f;     // slop band around the hold range where the bot just stands
        private const float BackoffThrottle = 1.0f;    // back off at FULL speed so an approaching attacker can't just fill the gap
        private const float MoveChangeDelay = 0.2f;    // reaction beat before adopting a CLOSER hold range (advancing); retreating is immediate
        private const float MoveHysteresis = 0.5f;     // ignore range jitter smaller than this when deciding "advance"
        private const float StrikerLockRange = 3f;     // attacker-lock only triggers for strikers within this (when targetRange is unlimited) - a distant swing at someone else shouldn't grab us

        // ---- Difficulty / personality levers (IConfigurableAi; defaults per preset in DefaultLeversFor) ----
        private float _offensiveBase, _offensiveVar;   // close spacing to press/attack from (base + jitter)
        private float _defensiveBase, _defensiveVar;   // further spacing to guard/read from (base + jitter)
        private float _attackRange;                    // within this distance a stab can land, so we throw
        private float _attackReadBeat;                 // extra randomized read/decision beat on the attack cooldown
        private float _riposteReactionMin, _riposteReactionMax; // human reaction beat between a block landing and the counter
        private float _riposteWindow;                  // how long the post-block counter stays available
        private float _blockReactionMin, _blockReactionMax;     // reaction beat between reading an attack and raising the guard (0 = instant)

        // Capability toggles (levers).
        private bool _press;      // throw the first blow when the enemy isn't threatening
        private bool _riposte;    // counter after our guard absorbs a committed attack
        private bool _move;       // drive melee spacing (false = stand our ground)
        private bool _pursue;     // advance toward a target that's too far (false = hold ground, let it walk away)
        private bool _engageOnAttack; // start PASSIVE (block only); only fight a player who attacks us, and only until they die

        // Targeting levers (see ResolveTarget).
        private float _targetRange;   // only acquire candidates within this range (<= 0 = unlimited)
        private bool _ignoreTeam;     // target any player regardless of faction (else enemies only)
        private bool _stickyTarget;   // keep the current target while valid (else re-pick the closest each tick)

        private readonly BotAiEnum _aiType;
        private float _offensiveRange;                // close spacing actually in use (re-rolled for jitter)
        private float _defensiveRange;                // further spacing actually in use (re-rolled for jitter)
        private float _appliedRange;                  // the hold range actually driving movement (lags on advancing)
        private float _advanceWantedSince = -1f;      // realtime we first wanted to advance to a closer range (-1 = not)

        private int? _assignedTargetId;       // higher-layer pin (ITargetableAi); preferred over nearest while alive
        private int? _targetId;               // last resolved target (sticky or closest, per _stickyTarget)
        private int? _lockTargetId;           // attacker-lock: a mid-strike player we stick to through the exchange
        private float _lockUntil;             // realtime the attacker-lock expires
        private bool _engaged;                // engageOnAttack runtime: provoked and fighting (vs passive). NOT inherited - a Replace replacement starts passive.
        private int? _engagedTargetId;        // engageOnAttack runtime: the attacker we fight until it (or we) die
        private bool _stancePending = true;   // issue EnableCombatStance once, on the first spawned tick
        private bool _runPending = true;      // establish the sticky run toggle once, on first engagement
        private string _blockToken;           // the block playerAction we're currently holding (null = not blocking)

        // Fight/riposte attack sequencer state (see StepAttack).
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
        private float _blockDesiredSince = -1f; // realtime we first wanted THIS guard (for block reaction; -1 = not)
        private float _blockReadyAt;          // realtime the guard may go up (start + block reaction beat)
        private float _rerollAt;              // realtime to next re-roll the hold distance

        public MeleeAi(BotAiEnum aiType)
        {
            _aiType = aiType;
            // Each lever = its global default (settable) or the preset built-in; TrySet does the typed parse.
            foreach (var (name, def) in DefaultLeversFor(aiType))
                if (!TrySet(name, GlobalAiConfigurable.Default(aiType.ToString(), name, def), out _))
                    TrySet(name, def, out _);
            RollHoldRange();
        }

        public BotAiEnum AiType => _aiType;

        // Built-in lever defaults per preset. Shared tuned values, then the capability toggles that make the
        // preset. GlobalAiConfigurable seeds the global defaults from this, so 'rc get globalAI' reports them.
        public static (string name, string value)[] DefaultLeversFor(BotAiEnum aiType)
        {
            var levers = new List<(string, string)>
            {
                ("offensiveRange", "0.7"), ("offensiveRangeVariance", "0.1"),
                ("defensiveRange", "2.0"), ("defensiveRangeVariance", "0.4"),
                ("attackRange", "2.0"), ("attackReadBeat", "0.6"),
                ("riposteReactionMin", "0"), ("riposteReactionMax", "0.5"), ("riposteWindow", "0.6"),
                ("blockReactionMin", "0.1"), ("blockReactionMax", "0.2"), // a human-like beat before the guard goes up
                ("ignoreTeam", "on"),                                   // target anyone by default (drill utility - no need to be on the opposing faction)
            };
            switch (aiType)
            {
                case BotAiEnum.MeleeFight:
                    levers.Add(("press", "on"));  levers.Add(("riposte", "on"));  levers.Add(("move", "on")); levers.Add(("pursue", "on"));
                    levers.Add(("targetRange", "0")); levers.Add(("stickyTarget", "on")); levers.Add(("engageOnAttack", "off")); break;
                case BotAiEnum.Sparring:
                    // Only engages players who come close, faces the closest, and gives up when they leave.
                    levers.Add(("press", "off")); levers.Add(("riposte", "on"));  levers.Add(("move", "off")); levers.Add(("pursue", "off"));
                    levers.Add(("targetRange", "4")); levers.Add(("stickyTarget", "off")); levers.Add(("engageOnAttack", "off")); break;
                case BotAiEnum.Guardian:
                    // Passive (block only) until a player in range strikes it; then it locks onto that attacker and
                    // fights (these fight levers) until the attacker dies, then returns to passive. targetRange is
                    // the passive read/provoke range; while engaged the target is tracked regardless of range.
                    levers.Add(("press", "on"));  levers.Add(("riposte", "on"));  levers.Add(("move", "on")); levers.Add(("pursue", "on"));
                    levers.Add(("targetRange", "4")); levers.Add(("stickyTarget", "off")); levers.Add(("engageOnAttack", "on")); break;
                default: // MeleeDefend
                    // Holds ground and blocks but does NOT chase, so a player can back off to disengage; drops
                    // the target entirely once they're past targetRange.
                    levers.Add(("press", "off")); levers.Add(("riposte", "off")); levers.Add(("move", "on")); levers.Add(("pursue", "off"));
                    levers.Add(("targetRange", "6")); levers.Add(("stickyTarget", "on")); levers.Add(("engageOnAttack", "off")); break;
            }
            return levers.ToArray();
        }

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

            float now = Time.realtimeSinceStartup;
            IPlayer target = ResolveTarget(self, now);
            if (target?.PlayerObject == null)
            {
                _attackPhase = AttackPhase.None; // don't resume a stale chamber when a target is reacquired
                return DropBlock(new BotIntent { MoveAxis = Vector2.zero }); // no enemy: stand, lower guard
            }

            Vector3 tp = target.PlayerObject.transform.position;
            Vector2 targetPos = new Vector2(tp.x, tp.z);
            CombatTracker.TryGet(target.PlayerId, out CombatTracker.MeleeState enemy);

            // Passive vs. engaged (engageOnAttack): while passive (not yet provoked) suppress attacking and
            // chasing this tick, so the bot only faces and blocks. ResolveTarget flips _engaged. When
            // engageOnAttack is off, passive is always false and these are just the raw levers.
            bool passive = _engageOnAttack && !_engaged;
            bool press = _press && !passive;
            bool riposte = _riposte && !passive;
            bool pursue = _pursue && !passive;

            // Face the target's ACTUAL position - leading where it's going made the bot over-rotate/spin up
            // close. While striking, nudge the aim sideways to compensate for the stab coming off the RIGHT of
            // the body, so the thrust lands on centre of mass.
            Vector2 aimPos = targetPos;
            if (_attackPhase == AttackPhase.Chamber || now < _strikeCommittedUntil)
            {
                Vector2 toTarget = targetPos - pose.Position;
                if (toTarget.sqrMagnitude > 1e-4f)
                {
                    Vector2 dir = toTarget.normalized;
                    Vector2 botLeft = new Vector2(-dir.y, dir.x); // bot's left; shift aim here to centre a right-hand stab
                    aimPos = targetPos + botLeft * AimOffset;
                }
            }
            BotIntent intent = new BotIntent { LookHeading = MovementSolver.HeadingTo(pose.Position, aimPos) };

            // Stab priority: after our guard ABSORBS the enemy's committed stab they're recovering and can't beat
            // our counter, so we hand ourselves a brief window to riposte at once (ignoring the attack cooldown).
            // Priority comes ONLY from the engine's block event - a real absorbed hit, never a guess.
            bool priority = false;
            if (riposte)
            {
                float myBlock = CombatTracker.LastBlockTime(self.PlayerId);
                if (myBlock > _lastConsumedBlock)
                {
                    _lastConsumedBlock = myBlock;
                    _riposteReadyAt = now + Random.Range(_riposteReactionMin, _riposteReactionMax); // reaction beat before we counter
                    _priorityUntil = _riposteReadyAt + _riposteWindow;                              // counter window runs AFTER the beat
                }

                // But only actually riposte if the enemy has NOT readied a fresh stab since that block: not
                // winding one up now, and no windup newer than the block we absorbed. Otherwise they're holding a
                // chambered stab (or just released one our guard hasn't caught yet) and would spear us the instant
                // the guard drops - the feint-then-hold exploit. Keep blocking; we re-earn priority when our
                // guard absorbs THAT stab too.
                priority = now >= _riposteReadyAt && now < _priorityUntil && !enemy.WindingUp && enemy.WindupTime <= _lastConsumedBlock;
            }

            // Vary the spacing over time so the bot isn't pinned to one radius (feels more like a real duellist).
            if (now >= _rerollAt) { RollHoldRange(); _rerollAt = now + Random.Range(1.5f, 3.5f); }

            // While our OWN strike is still flying, we must not block - a block cancels the swing before it lands.
            bool committed = now < _strikeCommittedUntil;

            // "I threw first" read: our windup began before the enemy started theirs, so our stab lands first -
            // commit it rather than bailing to a block. Using IsThreat (not just WindingUp) so an instant
            // reaction-throw still counts as "they went second".
            bool chamberCommit = (press || riposte) && _attackPhase == AttackPhase.Chamber
                                 && enemy.IsThreat(now) && _chamberStartedAt <= enemy.WindupTime;
            if (chamberCommit) _threwFirst = true; // we out-timed them - commit harder to this swing (see StepAttack)

            string desiredBlock = (priority || committed || chamberCommit) ? null : DesiredBlockToken(enemy, now);

            // Block reaction: a real player takes a beat to raise the guard after reading the attack. Applies
            // only to the INITIAL raise (switching guard direction once up stays instant). min=max=0 = instant.
            if (desiredBlock != null)
            {
                if (_blockDesiredSince < 0f)
                {
                    _blockDesiredSince = now;
                    _blockReadyAt = now + Random.Range(_blockReactionMin, _blockReactionMax);
                }
                if (_blockToken == null && now < _blockReadyAt)
                    desiredBlock = null; // still reacting; guard not up yet
            }
            else
            {
                _blockDesiredSince = -1f;
            }

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
                intent.MoveAxis = _move ? HoldRange(pose, targetPos, MovementRange(false, now), pursue) : Vector2.zero;
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
                // Free: 'press' closes to the OFFENSIVE range; otherwise hold the reading distance.
                intent.MoveAxis = _move ? HoldRange(pose, targetPos, MovementRange(press, now), pursue) : Vector2.zero;

                // Attack when the enemy isn't threatening. With 'priority' this is the post-block riposte and
                // fires immediately (ignoring cooldown/press); otherwise it's throwing FIRST, gated by 'press'.
                // Skip the tick we drop the block (StopMeleeBlock took the action channel; strike resumes next).
                // Also call while a chamber is in progress even if press/riposte just went off (e.g. a Guardian
                // whose target died mid-swing drops to passive) so the held MeleeStrike is RELEASED cleanly
                // instead of being left to auto-cycle.
                if ((press || riposte || _attackPhase == AttackPhase.Chamber) && !droppedBlock)
                    StepAttack(ref intent, pose, targetPos, priority, press);
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

        // Attack sequencer. A strike is ONE MeleeStrike{dir} (starts + holds the windup), silence while it holds,
        // then ONE ExecuteMeleeWeaponStrike to release it, then a cooldown. Blocking pre-empts this (in Decide).
        // 'priority' (a post-block riposte) bypasses both 'press' and the cooldown; a non-priority strike is the
        // bot THROWING FIRST and only fires when 'press' is enabled (effective press, gated by passive state).
        private void StepAttack(ref BotIntent intent, BotPose pose, Vector2 targetPos, bool priority, bool press)
        {
            float now = Time.realtimeSinceStartup;

            if (_attackPhase == AttackPhase.Chamber)
            {
                // The windup is held by the SINGLE MeleeStrike already sent - re-sending it every tick restarts
                // the windup animation (the glitch), so while holding we issue NOTHING. One ExecuteMeleeWeaponStrike
                // releases it and ends the swing cleanly (so it can't then auto-cycle).
                if (now >= _executeAt)
                {
                    intent.Action = "ExecuteMeleeWeaponStrike";
                    _attackPhase = AttackPhase.None;
                    _attackCooldownUntil = now + MissedStabDuration + Random.Range(0f, _attackReadBeat);
                    _priorityUntil = 0f;                              // riposte thrown - priority spent
                    // Commit to the swing (blocking now would cancel it). If we THREW FIRST, commit LONGER so the
                    // bot backs its own stab as it lands instead of flinching into a guard and eating the trade.
                    _strikeCommittedUntil = now + StrikeCommitWindow + (_threwFirst ? FirstStrikeCommitBonus : 0f);
                }
                return;
            }

            // Idle: begin a strike if allowed + close enough. A priority riposte ignores press/cooldown; a
            // non-priority strike (throwing first) needs 'press' and respects the cooldown.
            if (!priority)
            {
                if (!press) return;
                if (now < _attackCooldownUntil) return;
            }
            if ((targetPos - pose.Position).sqrMagnitude > _attackRange * _attackRange) return;

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
            _offensiveRange = _offensiveBase + Random.Range(0f, _offensiveVar);
            _defensiveRange = _defensiveBase + Random.Range(0f, _defensiveVar);
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
        // the tolerance band. Re-solved each tick against the live pose since the target moves. When 'pursue' is
        // off we DON'T close a gap that's too big (we only ever hold or back off), so a retreating player can walk
        // away and disengage instead of being followed forever.
        private static Vector2 HoldRange(BotPose pose, Vector2 targetPos, float range, bool pursue)
        {
            Vector2 toTarget = targetPos - pose.Position;
            float dist = toTarget.magnitude;
            if (dist < 1e-4f) return Vector2.zero;

            Vector2 dir = toTarget / dist;
            if (dist > range + RangeTolerance) return pursue ? MovementSolver.ToLocalAxis(pose, dir, 1f) : Vector2.zero;
            if (dist < range - RangeTolerance) return MovementSolver.ToLocalAxis(pose, -dir, BackoffThrottle);
            return Vector2.zero;
        }

        // ---- ITargetableAi ----

        // Pin a preferred target (a higher-layer supervisor's seam for target control / sparring), or null to
        // clear the pin and fall back to auto-acquiring the nearest enemy.
        public void SetTarget(int? playerId) => _assignedTargetId = playerId;

        // Target resolution, in priority order:
        //  1. External pin (ITargetableAi) - a supervisor's explicit choice; wins while it's a live candidate.
        //  2. Attacker-lock - once a candidate begins a strike we lock onto it through the exchange (plus a tail
        //     that covers our riposte), regardless of who's now closer, so we don't get pulled off an attacker.
        //  3. Sticky - keep the current target while it's still a valid candidate (Fight/Defend duel one foe).
        //  4. Otherwise the nearest candidate (Sparring, stickyTarget off, re-picks the closest in range).
        // A "candidate" is a spawned OTHER player, filtered by team (unless ignoreTeam) and by targetRange.
        private IPlayer ResolveTarget(BotController self, float now)
        {
            // 1. External pin.
            if (_assignedTargetId is int pinned)
            {
                IPlayer p = StateTracker.GetPlayerById(pinned);
                if (IsCandidate(self, p, ignoreRange: true)) return p;
            }

            // 1b. engageOnAttack (Guardian): a passive/engaged state machine that fully owns targeting.
            if (_engageOnAttack)
                return ResolveEngageOnAttack(self, now);

            // 2. Attacker-lock. Hold it (ignoring range/closest) while live and unexpired, refreshing while that
            //    player is still striking. Don't (re)acquire a lock mid-OWN-swing, so we finish our stab on the
            //    current target rather than hopping aim to a new attacker.
            if (_lockTargetId is int locked)
            {
                IPlayer lp = StateTracker.GetPlayerById(locked);
                if (now < _lockUntil && IsCandidate(self, lp, ignoreRange: true))
                {
                    if (CombatTracker.TryGet(locked, out CombatTracker.MeleeState ls) && ls.IsThreat(now))
                        _lockUntil = now + LockTail;
                    _targetId = locked;
                    return lp;
                }
                _lockTargetId = null;
            }

            bool ownSwing = _attackPhase == AttackPhase.Chamber || now < _strikeCommittedUntil;
            if (!ownSwing)
            {
                IPlayer striker = FindNearestStriker(self, now);
                if (striker != null)
                {
                    _lockTargetId = striker.PlayerId;
                    _lockUntil = now + LockTail;
                    _targetId = striker.PlayerId;
                    return striker;
                }
            }

            // 3. Sticky.
            if (_stickyTarget && _targetId is int cur)
            {
                IPlayer c = StateTracker.GetPlayerById(cur);
                if (IsCandidate(self, c, ignoreRange: false)) return c;
            }

            // 4. Nearest candidate in range.
            IPlayer nearest = FindNearestCandidate(self);
            _targetId = nearest?.PlayerId;
            return nearest;
        }

        // engageOnAttack targeting (Guardian). PASSIVE: face/block the nearest candidate within targetRange but
        // don't fight (Decide gates press/riposte/pursue off while passive). The moment a candidate in that range
        // strikes, ENGAGE it - lock it as the target and fight (Decide re-enables the fight levers) until it dies,
        // tracked regardless of range. When the engaged target dies/leaves we drop back to passive. _engaged is
        // runtime only (not inherited), so a Replace replacement starts passive again.
        private IPlayer ResolveEngageOnAttack(BotController self, float now)
        {
            if (_engaged && _engagedTargetId is int et)
            {
                IPlayer t = StateTracker.GetPlayerById(et);
                if (IsCandidate(self, t, ignoreRange: true)) // still alive/valid: stay engaged, locked past range
                {
                    _targetId = et;
                    return t;
                }
                _engaged = false;          // target died or left the game - back to passive
                _engagedTargetId = null;
            }

            // Passive: a striker within our (small) targetRange provokes us into engaging that attacker.
            IPlayer striker = FindNearestStriker(self, now);
            if (striker != null)
            {
                _engaged = true;
                _engagedTargetId = striker.PlayerId;
                _targetId = striker.PlayerId;
                return striker;
            }

            // Nobody attacking: just face/block the nearest player in range.
            IPlayer near = FindNearestCandidate(self);
            _targetId = near?.PlayerId;
            return near;
        }

        // How long an attacker-lock outlives the strike: long enough to land our riposte (reaction + window + a beat).
        private float LockTail => _riposteReactionMax + _riposteWindow + 0.3f;

        // Closest candidate currently mid-strike (winding up or in a committed lethal window) within lock range,
        // or null. Lock range = targetRange, or StrikerLockRange when targeting is unlimited so a distant swing
        // at someone else can't grab us. Once locked, the lock holds on regardless of range (see ResolveTarget).
        private IPlayer FindNearestStriker(BotController self, float now)
        {
            if (!(self.Position is Vector3 selfPos)) return null;

            float range = _targetRange > 0f ? _targetRange : StrikerLockRange;
            float rangeSqr = range * range;

            IPlayer nearest = null;
            float bestSqr = float.MaxValue;

            var players = StateTracker.AllPlayers;
            for (int i = 0; i < players.Count; i++)
            {
                IPlayer p = players[i];
                if (!IsCandidate(self, p, ignoreRange: true)) continue; // team/spawned; range applied here
                if (!CombatTracker.TryGet(p.PlayerId, out CombatTracker.MeleeState st) || !st.IsThreat(now)) continue;

                float sqr = (p.PlayerObject.transform.position - selfPos).sqrMagnitude;
                if (sqr > rangeSqr) continue;
                if (sqr < bestSqr) { bestSqr = sqr; nearest = p; }
            }

            return nearest;
        }

        private IPlayer FindNearestCandidate(BotController self)
        {
            if (!(self.Position is Vector3 selfPos)) return null;

            IPlayer nearest = null;
            float bestSqr = float.MaxValue;

            var players = StateTracker.AllPlayers;
            for (int i = 0; i < players.Count; i++)
            {
                IPlayer p = players[i];
                if (!IsCandidate(self, p, ignoreRange: false)) continue;

                float sqr = (p.PlayerObject.transform.position - selfPos).sqrMagnitude;
                if (sqr < bestSqr) { bestSqr = sqr; nearest = p; }
            }

            return nearest;
        }

        // A targetable player: spawned and ALIVE (a corpse is skipped), not us, on a hostile faction (unless
        // ignoreTeam targets anyone) and - unless ignoreRange - within targetRange (targetRange <= 0 = unlimited).
        private bool IsCandidate(BotController self, IPlayer p, bool ignoreRange)
        {
            if (p == null || p.PlayerObject == null || !p.IsAlive || p.PlayerId == self.PlayerId) return false;

            if (!_ignoreTeam)
            {
                if (!p.Faction.HasValue || !self.Bot.Faction.HasValue || p.Faction.Value == self.Bot.Faction.Value)
                    return false;
            }

            if (!ignoreRange && _targetRange > 0f)
            {
                if (!(self.Position is Vector3 selfPos)) return false;
                if ((p.PlayerObject.transform.position - selfPos).sqrMagnitude > _targetRange * _targetRange)
                    return false;
            }

            return true;
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

        // ---- IConfigurableAi ----

        private static readonly string[] LeverNames =
        {
            "offensiveRange", "offensiveRangeVariance", "defensiveRange", "defensiveRangeVariance",
            "attackRange", "attackReadBeat", "riposteReactionMin", "riposteReactionMax", "riposteWindow",
            "blockReactionMin", "blockReactionMax", "press", "riposte", "move", "pursue",
            "targetRange", "ignoreTeam", "stickyTarget", "engageOnAttack"
        };

        public bool TrySet(string name, string value, out string error)
        {
            error = string.Empty;
            switch (name.ToLowerInvariant())
            {
                case "offensiverange":         return SetFloat(value, 0f, v => _offensiveBase = v, "offensiveRange", ref error);
                case "offensiverangevariance": return SetFloat(value, 0f, v => _offensiveVar = v, "offensiveRangeVariance", ref error);
                case "defensiverange":         return SetFloat(value, 0f, v => _defensiveBase = v, "defensiveRange", ref error);
                case "defensiverangevariance": return SetFloat(value, 0f, v => _defensiveVar = v, "defensiveRangeVariance", ref error);
                case "attackrange":            return SetFloat(value, 0f, v => _attackRange = v, "attackRange", ref error);
                case "attackreadbeat":         return SetFloat(value, 0f, v => _attackReadBeat = v, "attackReadBeat", ref error);
                case "ripostereactionmin":     return SetFloat(value, 0f, v => _riposteReactionMin = v, "riposteReactionMin", ref error);
                case "ripostereactionmax":     return SetFloat(value, 0f, v => _riposteReactionMax = v, "riposteReactionMax", ref error);
                case "ripostewindow":          return SetFloat(value, 0f, v => _riposteWindow = v, "riposteWindow", ref error);
                case "blockreactionmin":       return SetFloat(value, 0f, v => _blockReactionMin = v, "blockReactionMin", ref error);
                case "blockreactionmax":       return SetFloat(value, 0f, v => _blockReactionMax = v, "blockReactionMax", ref error);
                case "press":   return SetBool(value, v => _press = v, "press", ref error);
                case "riposte": return SetBool(value, v => _riposte = v, "riposte", ref error);
                case "move":    return SetBool(value, v => _move = v, "move", ref error);
                case "pursue":  return SetBool(value, v => _pursue = v, "pursue", ref error);
                case "targetrange":  return SetFloat(value, 0f, v => _targetRange = v, "targetRange", ref error); // 0 = unlimited
                case "ignoreteam":   return SetBool(value, v => _ignoreTeam = v, "ignoreTeam", ref error);
                case "stickytarget": return SetBool(value, v => _stickyTarget = v, "stickyTarget", ref error);
                case "engageonattack": return SetBool(value, v => _engageOnAttack = v, "engageOnAttack", ref error);
                default:
                    error = $"Unknown lever '{name}'. MeleeAi levers: {string.Join(", ", LeverNames)}.";
                    return false;
            }
        }

        public IEnumerable<(string name, string value)> ListParams()
        {
            yield return ("offensiveRange", _offensiveBase.ToString("0.##"));
            yield return ("offensiveRangeVariance", _offensiveVar.ToString("0.##"));
            yield return ("defensiveRange", _defensiveBase.ToString("0.##"));
            yield return ("defensiveRangeVariance", _defensiveVar.ToString("0.##"));
            yield return ("attackRange", _attackRange.ToString("0.##"));
            yield return ("attackReadBeat", _attackReadBeat.ToString("0.##"));
            yield return ("riposteReactionMin", _riposteReactionMin.ToString("0.##"));
            yield return ("riposteReactionMax", _riposteReactionMax.ToString("0.##"));
            yield return ("riposteWindow", _riposteWindow.ToString("0.##"));
            yield return ("blockReactionMin", _blockReactionMin.ToString("0.##"));
            yield return ("blockReactionMax", _blockReactionMax.ToString("0.##"));
            yield return ("press", _press ? "on" : "off");
            yield return ("riposte", _riposte ? "on" : "off");
            yield return ("move", _move ? "on" : "off");
            yield return ("pursue", _pursue ? "on" : "off");
            yield return ("targetRange", _targetRange.ToString("0.##"));
            yield return ("ignoreTeam", _ignoreTeam ? "on" : "off");
            yield return ("stickyTarget", _stickyTarget ? "on" : "off");
            yield return ("engageOnAttack", _engageOnAttack ? "on" : "off");
        }

        private static bool SetFloat(string value, float min, System.Action<float> set, string name, ref string error)
        {
            if (!float.TryParse(value, out float v) || v < min)
            {
                error = $"{name} must be a number{(min > float.MinValue ? $" >= {min}" : "")}.";
                return false;
            }
            set(v);
            return true;
        }

        private static bool SetBool(string value, System.Action<bool> set, string name, ref string error)
        {
            switch (value.ToLowerInvariant())
            {
                case "on": case "true": case "1": case "yes": set(true); return true;
                case "off": case "false": case "0": case "no": set(false); return true;
                default: error = $"{name} must be on or off."; return false;
            }
        }

        // Carry the per-bot lever/toggle overrides (and any pinned target) to a Replace-policy replacement so a
        // bot tuned with 'rc bot cfg' isn't reset to preset defaults on death.
        public void InheritFrom(IBotAi previous)
        {
            if (!(previous is MeleeAi p)) return;

            _offensiveBase = p._offensiveBase; _offensiveVar = p._offensiveVar;
            _defensiveBase = p._defensiveBase; _defensiveVar = p._defensiveVar;
            _attackRange = p._attackRange; _attackReadBeat = p._attackReadBeat;
            _riposteReactionMin = p._riposteReactionMin; _riposteReactionMax = p._riposteReactionMax;
            _riposteWindow = p._riposteWindow;
            _blockReactionMin = p._blockReactionMin; _blockReactionMax = p._blockReactionMax;
            _press = p._press; _riposte = p._riposte; _move = p._move; _pursue = p._pursue;
            _targetRange = p._targetRange; _ignoreTeam = p._ignoreTeam; _stickyTarget = p._stickyTarget;
            _engageOnAttack = p._engageOnAttack; // carry the lever; _engaged/_engagedTargetId are NOT carried, so a replacement starts passive
            _assignedTargetId = p._assignedTargetId;
            RollHoldRange();
        }
    }
}
