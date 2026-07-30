using System.Collections.Generic;
using UnityEngine;
using MDS.ConfigVariables;

namespace MDS.Systems
{
    // Melee combat AI. It faces its target, holds spacing, and blocks the target's attacks with the guard that
    // counters their windup direction (BlockTokenFor). Depending on its toggles it also presses in to attack and
    // ripostes after a block. Perception comes from CombatTracker (the target's melee state, read from packets);
    // it acts through the BotIntent action channel, using block and strike tokens verified on a live bot with
    // 'rc bot act'.
    //
    // One class, several presets selected by BotAiEnum and built from lever bundles in DefaultLeversFor:
    //   RiposteDummy stands its ground, blocks, and only counters once provoked (press off, riposte on).
    //   DuelingEasy / DuelingNormal / Dueling wait passively, blocking the nearest player, until someone attacks
    //     them; then they lock that attacker and fight to the death before returning to passive. The tiers differ
    //     only in reaction speed. A Replace replacement starts passive again.
    // StabbingDummy is a separate class (MeleeDummy), a static stabber with no perception.
    //
    // Every tuning value is a lever (IConfigurableAi), settable per bot with 'rc bot cfg' and defaulting from
    // globalAI. The strike-mechanic constants below are not levers: they encode how the engine plays a stab out,
    // measured in-game, so changing them just breaks the bot.
    //
    // Targeting is lever-driven too (ResolveTarget): targetRange gates who it engages, ignoreTeam and ignoreBots
    // filter by faction and human-vs-bot, and stickyTarget picks holding one foe versus the closest each tick. An
    // automatic attacker-lock keeps it on whoever is mid-strike, and an ITargetableAi pin lets a future supervisor
    // override the choice.
    //
    // Strike quirk: a raw MeleeStrike token latches an auto-cycling attack loop, so a strike is a short held
    // chamber (one MeleeStrike) released by a single ExecuteMeleeWeaponStrike, which also stops the cycle. See
    // StepAttack.
    public class MeleeAi : IBotAi, IConfigurableAi, ITargetableAi
    {
        // Strike-mechanic timings, measured from the engine. Not levers.
        private const float WindupSeconds = 0.15f;    // hold the windup this long (one MeleeStrike) before releasing
        // A committed stab occupies the bot about this long before it can throw again, whether it misses or is
        // blocked (both measured near 1.5s from a human spamming attack; the engine's ~0.35s block stun does not
        // shorten it). Throwing the next strike sooner overlaps the still-playing swing. Timed from release.
        private const float MissedStabDuration = 1.5f;
        private const float StrikeCommitWindow = 0.5f; // after releasing a strike, don't block or we cancel our own swing before it lands
        private const float FirstStrikeCommitBonus = 0.4f; // extra commit time when we threw first, so we back our stab instead of flinching into a guard
        private const float MinBlockHold = 0.35f;      // keep a raised guard up at least this long so it reads and animates
        private const float AimOffset = 0.3f;          // sideways aim shift while striking to centre a right-hand stab, metres

        // Movement feel. Kept const for now.
        private const float RangeTolerance = 0.3f;     // slop band around the hold range where the bot just stands
        private const float BackoffThrottle = 1.0f;    // back off at full speed so an approaching attacker can't just fill the gap
        private const float MoveChangeDelay = 0.2f;    // reaction beat before adopting a closer hold range; retreating is immediate
        private const float MoveHysteresis = 0.5f;     // ignore range jitter smaller than this when deciding to advance
        private const float StrikerLockRange = 3f;     // attacker-lock only triggers for strikers within this when targetRange is unlimited

        // Tuning levers (IConfigurableAi). Defaults per preset in DefaultLeversFor.
        private float _offensiveBase, _offensiveVar;   // close spacing to press and attack from (base plus jitter)
        private float _defensiveBase, _defensiveVar;   // further spacing to guard and read from (base plus jitter)
        private float _attackRange;                    // within this distance a stab can land, so we throw
        private float _attackReadBeat;                 // extra randomized beat on the attack cooldown
        private float _riposteReactionMin, _riposteReactionMax; // reaction beat between a block landing and the counter
        private float _riposteWindow;                  // how long the post-block counter stays available
        private float _blockReactionMin, _blockReactionMax;     // reaction beat between reading an attack and raising the guard (0 = instant)

        // Capability toggles (levers).
        private bool _press;      // throw the first blow when the enemy isn't threatening
        private bool _riposte;    // counter after our guard absorbs an attack
        private bool _move;       // drive melee spacing (false = stand our ground)
        private bool _pursue;     // advance toward a target that's too far (false = hold ground, let it walk away)
        private bool _engageOnAttack; // start passive (block only), only fight a player who attacks us, and only until they die

        // Targeting levers (see ResolveTarget).
        private float _targetRange;   // only acquire candidates within this range (<= 0 = unlimited)
        private bool _ignoreTeam;     // target any player regardless of faction (else enemies only)
        private bool _ignoreBots;     // target only human players (skip bots)
        private bool _stickyTarget;   // keep the current target while valid (else re-pick the closest each tick)
        private float _passiveRange;  // engageOnAttack: hold distance while waiting (engaged uses defensiveRange)

        private readonly BotAiEnum _aiType;
        private float _offensiveRange;                // close spacing actually in use, re-rolled for jitter
        private float _defensiveRange;                // further spacing actually in use, re-rolled for jitter
        private float _appliedRange;                  // the hold range actually driving movement (lags on advancing)
        private float _advanceWantedSince = -1f;      // realtime we first wanted to advance to a closer range (-1 = not)

        private int? _assignedTargetId;       // pin from ITargetableAi; preferred over nearest while alive
        private int? _targetId;               // last resolved target (sticky or closest, per _stickyTarget)
        private int? _lockTargetId;           // attacker-lock: a mid-strike player we stick to through the exchange
        private float _lockUntil;             // realtime the attacker-lock expires
        private bool _engaged;                // engageOnAttack runtime: provoked and fighting. Not inherited, so a replacement starts passive.
        private int? _engagedTargetId;        // engageOnAttack runtime: the attacker we fight until it (or we) die
        private float _lastEngageBlock;       // engageOnAttack runtime: block time we last engaged off (dedupe)
        private bool _stancePending = true;   // issue EnableCombatStance once, on the first spawned tick
        private bool _runPending = true;      // establish the sticky run toggle once, on first engagement
        private string _blockToken;           // the block playerAction we're currently holding (null = not blocking)

        // Attack sequencer state (see StepAttack).
        private enum AttackPhase { None, Chamber }
        private AttackPhase _attackPhase;
        private string _attackDir;            // "High" or "Low" for the strike in progress
        private float _attackCooldownUntil;   // realtime before which we won't start another strike
        private float _chamberStartedAt;      // realtime our windup began (for the "I threw first" read)
        private float _executeAt;             // realtime to release the held windup
        private bool _threwFirst;             // this swing out-timed the enemy's, so commit to it harder

        // Stab-priority state: after our guard absorbs an attack we get a brief riposte window (Decide).
        private float _lastConsumedBlock;     // last block we reacted to as defender (dedupe)
        private float _riposteReadyAt;        // realtime before which we hold the guard (reaction beat) before countering
        private float _priorityUntil;         // while now < this: riposte immediately, don't re-block
        private float _strikeCommittedUntil;  // while now < this: our own strike is in flight, don't block (it'd cancel it)
        private float _blockStartedAt;        // realtime the current guard went up (for MinBlockHold)
        private float _blockDesiredSince = -1f; // realtime we first wanted this guard (for block reaction; -1 = not)
        private float _blockReadyAt;          // realtime the guard may go up (start plus block reaction beat)
        private float _rerollAt;              // realtime to next re-roll the hold distance

        public MeleeAi(BotAiEnum aiType)
        {
            _aiType = aiType;
            // Each lever takes its global default if set, otherwise the preset built-in. TrySet does the typed parse.
            foreach (var (name, def) in DefaultLeversFor(aiType))
                if (!TrySet(name, GlobalAiConfigurable.Default(aiType.ToString(), name, def), out _))
                    TrySet(name, def, out _);
            RollHoldRange();
        }

        public BotAiEnum AiType => _aiType;

        // Built-in lever defaults per preset: shared spacing values, then the toggles, targeting, and reaction
        // beats that make each preset. GlobalAiConfigurable seeds the global defaults from this so 'rc get globalAI'
        // reports them.
        public static (string name, string value)[] DefaultLeversFor(BotAiEnum aiType)
        {
            var levers = new List<(string, string)>
            {
                ("offensiveRange", "0.7"), ("offensiveRangeVariance", "0.1"),
                ("defensiveRange", "2.0"), ("defensiveRangeVariance", "0.4"),
                ("attackRange", "2.0"), ("riposteWindow", "0.6"),
                ("ignoreTeam", "true"),  // target anyone by default; no need to be on the opposing faction to use a bot
                ("ignoreBots", "true"),  // only respond to human players by default
                ("passiveRange", "0.6"), // engageOnAttack waiting-mode hold distance
            };
            switch (aiType)
            {
                // Dueling family: passive (block only) until a player in range strikes it and it blocks the hit,
                // then it locks that attacker and fights to the death before returning to passive. targetRange is
                // the passive read/provoke range. The three tiers share everything but the reaction beats.
                case BotAiEnum.DuelingEasy:
                case BotAiEnum.DuelingNormal:
                case BotAiEnum.Dueling:
                    levers.Add(("press", "true"));  levers.Add(("riposte", "true"));  levers.Add(("move", "true")); levers.Add(("pursue", "true"));
                    levers.Add(("targetRange", "3")); levers.Add(("stickyTarget", "false")); levers.Add(("engageOnAttack", "true"));
                    switch (aiType)
                    {
                        case BotAiEnum.DuelingEasy:   // sluggish, beatable
                            levers.Add(("blockReactionMin", "0.3")); levers.Add(("blockReactionMax", "0.5"));
                            levers.Add(("riposteReactionMin", "0.2")); levers.Add(("riposteReactionMax", "0.8"));
                            levers.Add(("attackReadBeat", "0.9")); break;
                        case BotAiEnum.DuelingNormal: // human reactions, the tuned baseline
                            levers.Add(("blockReactionMin", "0.1")); levers.Add(("blockReactionMax", "0.2"));
                            levers.Add(("riposteReactionMin", "0")); levers.Add(("riposteReactionMax", "0.5"));
                            levers.Add(("attackReadBeat", "0.6")); break;
                        default:                      // Dueling: instant reactions and fastest pacing, the hardest
                            levers.Add(("blockReactionMin", "0")); levers.Add(("blockReactionMax", "0"));
                            levers.Add(("riposteReactionMin", "0")); levers.Add(("riposteReactionMax", "0"));
                            levers.Add(("attackReadBeat", "0.3")); break;
                    }
                    break;
                default: // RiposteDummy: stands its ground, blocks, counters only, engages the closest in range.
                    levers.Add(("press", "false")); levers.Add(("riposte", "true"));  levers.Add(("move", "false")); levers.Add(("pursue", "false"));
                    levers.Add(("targetRange", "3")); levers.Add(("stickyTarget", "false")); levers.Add(("engageOnAttack", "false"));
                    levers.Add(("blockReactionMin", "0.1")); levers.Add(("blockReactionMax", "0.2"));
                    levers.Add(("riposteReactionMin", "0")); levers.Add(("riposteReactionMax", "0.5"));
                    levers.Add(("attackReadBeat", "0.6")); break;
            }
            return levers.ToArray();
        }

        public BotIntent Decide(BotController self, float deltaTime)
        {
            if (!self.TryGetPose(out BotPose pose))
                return BotIntent.Idle; // not spawned, issue nothing

            // Enter combat stance once so the bot can block and strike. Consumed only once actually spawned.
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

            // While passive (engageOnAttack and not yet provoked) suppress attacking and chasing, so the bot only
            // faces and blocks. ResolveTarget flips _engaged. When engageOnAttack is off, passive is always false
            // and these are just the raw levers.
            bool passive = _engageOnAttack && !_engaged;
            bool press = _press && !passive;
            bool riposte = _riposte && !passive;
            bool pursue = _pursue && !passive;

            // Face the target's actual position; leading where it's going made the bot over-rotate up close. While
            // striking, nudge the aim sideways because the stab comes off the right of the body, so the thrust
            // lands on centre of mass.
            Vector2 aimPos = targetPos;
            if (_attackPhase == AttackPhase.Chamber || now < _strikeCommittedUntil)
            {
                Vector2 toTarget = targetPos - pose.Position;
                if (toTarget.sqrMagnitude > 1e-4f)
                {
                    Vector2 dir = toTarget.normalized;
                    Vector2 botLeft = new Vector2(-dir.y, dir.x); // shift aim to the bot's left to centre a right-hand stab
                    aimPos = targetPos + botLeft * AimOffset;
                }
            }
            BotIntent intent = new BotIntent { LookHeading = MovementSolver.HeadingTo(pose.Position, aimPos) };

            // Stab priority: after our guard absorbs the enemy's stab they're recovering and can't beat our
            // counter, so we give ourselves a brief window to riposte at once, ignoring the attack cooldown.
            // Priority comes only from the engine's block event, a real absorbed hit, never a guess.
            bool priority = false;
            if (riposte)
            {
                float myBlock = CombatTracker.LastBlockTime(self.PlayerId);
                if (myBlock > _lastConsumedBlock)
                {
                    _lastConsumedBlock = myBlock;
                    _riposteReadyAt = now + Random.Range(_riposteReactionMin, _riposteReactionMax); // reaction beat before we counter
                    _priorityUntil = _riposteReadyAt + _riposteWindow;                              // window runs after the beat
                }

                // Only riposte if the enemy has not readied a fresh stab since that block: not winding one up now,
                // and no windup newer than the block we absorbed. Otherwise they're holding a chambered stab (or
                // just released one our guard hasn't caught yet) and would spear us the instant the guard drops,
                // the feint-then-hold exploit. Keep blocking; we re-earn priority when our guard absorbs that stab.
                priority = now >= _riposteReadyAt && now < _priorityUntil && !enemy.WindingUp && enemy.WindupTime <= _lastConsumedBlock;
            }

            // Vary the spacing over time so the bot isn't pinned to one radius.
            if (now >= _rerollAt) { RollHoldRange(); _rerollAt = now + Random.Range(1.5f, 3.5f); }

            // While our own strike is still flying we must not block: a block cancels the swing before it lands.
            bool committed = now < _strikeCommittedUntil;

            // "I threw first" read: our windup began before the enemy started theirs, so our stab lands first and
            // we commit it rather than bailing to a block. Using IsThreat rather than WindingUp so an instant
            // reaction-throw still counts as them going second.
            bool chamberCommit = (press || riposte) && _attackPhase == AttackPhase.Chamber
                                 && enemy.IsThreat(now) && _chamberStartedAt <= enemy.WindupTime;
            if (chamberCommit) _threwFirst = true; // out-timed them, so commit harder to this swing (see StepAttack)

            string desiredBlock = (priority || committed || chamberCommit) ? null : DesiredBlockToken(enemy, now);

            // Block reaction: a real player takes a beat to raise the guard after reading the attack. It applies
            // only to the initial raise; switching guard direction once up stays instant. min = max = 0 is instant.
            if (desiredBlock != null)
            {
                if (_blockDesiredSince < 0f)
                {
                    _blockDesiredSince = now;
                    _blockReadyAt = now + Random.Range(_blockReactionMin, _blockReactionMax);
                }
                if (_blockToken == null && now < _blockReadyAt)
                    desiredBlock = null; // still reacting, guard not up yet
            }
            else
            {
                _blockDesiredSince = -1f;
            }

            // Minimum block hold: once the guard is up, keep it up briefly even if we'd now drop it to riposte, so
            // it reads and its animation completes. Never overrides committing our own in-flight strike.
            if (desiredBlock == null && _blockToken != null && !committed && !chamberCommit
                && now - _blockStartedAt < MinBlockHold)
                desiredBlock = _blockToken;

            if (desiredBlock != null)
            {
                // Under threat: block. Abort any in-progress strike, since raising a block cancels our own windup.
                _attackPhase = AttackPhase.None;
                if (_blockToken != desiredBlock)
                {
                    if (_blockToken == null) _blockStartedAt = now; // this guard just went up
                    intent.Action = desiredBlock;                  // start, or switch direction (no StopMeleeBlock needed)
                    _blockToken = desiredBlock;
                }
                // Guarding: hold the further defensive distance to make space to read, following a circling player
                // instead of freezing. While waiting (passive) hold the closer passiveRange instead.
                intent.MoveAxis = _move ? HoldRange(pose, targetPos, passive ? _passiveRange : MovementRange(false, now), pursue) : Vector2.zero;
            }
            else
            {
                // Not threatened, or riposting with priority: lower the guard and keep melee spacing.
                bool droppedBlock = false;
                if (_blockToken != null)
                {
                    intent.Action = "StopMeleeBlock";
                    _blockToken = null;
                    droppedBlock = true;
                }
                // Free: press closes to the offensive range, otherwise hold the reading distance. While waiting
                // (passive) hold the closer passiveRange so it doesn't back off far from an approaching player.
                intent.MoveAxis = _move ? HoldRange(pose, targetPos, passive ? _passiveRange : MovementRange(press, now), pursue) : Vector2.zero;

                // Attack when the enemy isn't threatening. With priority this is the post-block riposte and fires
                // immediately (ignoring cooldown and press), otherwise it's throwing first, gated by press. Skip
                // the tick we drop the block (StopMeleeBlock took the action channel; the strike resumes next tick).
                // Also call while a chamber is in progress even if press/riposte just went off (e.g. a Dueling bot
                // whose target died mid-swing drops to passive) so the held MeleeStrike is released cleanly instead
                // of being left to auto-cycle.
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

        // The block playerAction to hold this tick, or null to lower the guard. We block whenever the enemy is a
        // melee threat: winding up, or a committed swing still in its lethal window.
        private static string DesiredBlockToken(CombatTracker.MeleeState enemy, float now)
        {
            if (string.IsNullOrEmpty(enemy.WindupDir)) return null;
            if (!enemy.IsThreat(now)) return null;
            return BlockTokenFor(enemy.WindupDir);
        }

        // Maps the enemy's attack direction (in their frame) to the block the bot raises (in its frame). High and
        // Low are overhead and underhand, shared, so they match directly. Left and Right mirror: the duellists
        // face each other, so the attacker's right side is the defender's left, and vice versa.
        private static string BlockTokenFor(string windupDir)
        {
            switch (windupDir)
            {
                case "Left":  return "MeleeBlockRight";
                case "Right": return "MeleeBlockLeft";
                default:      return "MeleeBlock" + windupDir; // High / Low
            }
        }

        // Attack sequencer. A strike is one MeleeStrike{dir} to start and hold the windup, silence while it holds,
        // then one ExecuteMeleeWeaponStrike to release it, then a cooldown. Blocking pre-empts this (in Decide). A
        // priority riposte bypasses both press and the cooldown; a non-priority strike is throwing first and only
        // fires when press is enabled.
        private void StepAttack(ref BotIntent intent, BotPose pose, Vector2 targetPos, bool priority, bool press)
        {
            float now = Time.realtimeSinceStartup;

            if (_attackPhase == AttackPhase.Chamber)
            {
                // The windup is held by the single MeleeStrike already sent; re-sending it every tick restarts the
                // windup animation, so while holding we issue nothing. One ExecuteMeleeWeaponStrike releases it and
                // ends the swing cleanly so it can't then auto-cycle.
                if (now >= _executeAt)
                {
                    intent.Action = "ExecuteMeleeWeaponStrike";
                    _attackPhase = AttackPhase.None;
                    _attackCooldownUntil = now + MissedStabDuration + Random.Range(0f, _attackReadBeat);
                    _priorityUntil = 0f;                              // riposte thrown, priority spent
                    // Commit to the swing, since blocking now would cancel it. If we threw first, commit longer so
                    // the bot backs its own stab as it lands instead of flinching into a guard and eating the trade.
                    _strikeCommittedUntil = now + StrikeCommitWindow + (_threwFirst ? FirstStrikeCommitBonus : 0f);
                }
                return;
            }

            // Idle: begin a strike if allowed and close enough. A priority riposte ignores press and cooldown; a
            // non-priority strike (throwing first) needs press and respects the cooldown.
            if (!priority)
            {
                if (!press) return;
                if (now < _attackCooldownUntil) return;
            }
            // A press attack only commits inside attackRange. A priority riposte always throws at the target
            // regardless of range, so a stationary RiposteDummy still counters an attacker who has backed off.
            if (!priority && (targetPos - pose.Position).sqrMagnitude > _attackRange * _attackRange) return;

            // Start the windup with one MeleeStrike, then hold with silence and release on the timer above.
            _attackDir = Random.value < 0.5f ? "High" : "Low"; // bayonet is High/Low; unblockable during recovery anyway
            _chamberStartedAt = now;
            _executeAt = now + WindupSeconds;
            _threwFirst = false;                               // set true only if we out-time the enemy this windup
            _attackPhase = AttackPhase.Chamber;
            intent.Action = "MeleeStrike" + _attackDir;
        }

        // Re-rolls the offensive and defensive spacings with a little jitter, so the bot varies its distance over
        // time instead of orbiting at a fixed radius.
        private void RollHoldRange()
        {
            _offensiveRange = _offensiveBase + Random.Range(0f, _offensiveVar);
            _defensiveRange = _defensiveBase + Random.Range(0f, _defensiveVar);
        }

        // The hold range to move toward this tick. Adopting a closer range (advancing) waits a short reaction beat,
        // like a player backing up takes a moment before running in; backing off applies immediately. Jitter below
        // MoveHysteresis doesn't count as a change of strategy.
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

        // Local input axis to sit at the given hold range: close in if too far, ease back if too close, stand
        // inside the tolerance band. Re-solved each tick against the live pose since the target moves. With pursue
        // off we don't close a gap that's too big (only hold or back off), so a retreating player can walk away
        // and disengage instead of being followed.
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

        // Pin a preferred target (a higher-layer supervisor's seam for target control), or null to clear the pin
        // and fall back to auto-acquiring the nearest enemy.
        public void SetTarget(int? playerId) => _assignedTargetId = playerId;

        // Target resolution, in priority order:
        //  1. External pin (ITargetableAi), a supervisor's explicit choice, wins while it's a live candidate.
        //  2. Attacker-lock: once a candidate begins a strike we lock onto it through the exchange plus a tail that
        //     covers our riposte, regardless of who's now closer, so we don't get pulled off an attacker.
        //  3. Sticky: keep the current target while it's still a valid candidate.
        //  4. Otherwise the nearest candidate (used when stickyTarget is off, re-picking the closest in range).
        // A candidate is a spawned, living other player, filtered by team (unless ignoreTeam) and by targetRange.
        private IPlayer ResolveTarget(BotController self, float now)
        {
            if (_assignedTargetId is int pinned)
            {
                IPlayer p = StateTracker.GetPlayerById(pinned);
                if (IsCandidate(self, p, ignoreRange: true)) return p;
            }

            // engageOnAttack (Dueling) is a passive/engaged state machine that fully owns targeting.
            if (_engageOnAttack)
                return ResolveEngageOnAttack(self, now);

            // Attacker-lock. Hold it (ignoring range and closest) while live and unexpired, refreshing while that
            // player is still striking. Don't acquire a new lock mid-own-swing, so we finish our stab on the
            // current target instead of hopping aim to a new attacker.
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

            if (_stickyTarget && _targetId is int cur)
            {
                IPlayer c = StateTracker.GetPlayerById(cur);
                if (IsCandidate(self, c, ignoreRange: false)) return c;
            }

            IPlayer nearest = FindNearestCandidate(self);
            _targetId = nearest?.PlayerId;
            return nearest;
        }

        // engageOnAttack targeting (Dueling). While passive it faces and blocks the nearest candidate within
        // targetRange but doesn't fight (Decide gates press/riposte/pursue off). It engages only the player whose
        // attack our guard actually blocks, a hit aimed at us, rather than anyone swinging within range, which
        // would grab players from other fights. Once engaged it locks that attacker and fights until it dies,
        // tracked regardless of range, then drops back to passive. _engaged is runtime only (not inherited), so a
        // Replace replacement starts passive again.
        private IPlayer ResolveEngageOnAttack(BotController self, float now)
        {
            if (_engaged && _engagedTargetId is int et)
            {
                IPlayer t = StateTracker.GetPlayerById(et);
                if (IsCandidate(self, t, ignoreRange: true)) // still alive and valid: stay engaged, locked past range
                {
                    _targetId = et;
                    return t;
                }
                _engaged = false;          // target died or left the game, back to passive
                _engagedTargetId = null;
            }

            // Engage the player whose strike we just blocked (confirmed aimed at us), by id from the block event,
            // so a swing at someone else nearby never provokes us.
            float myBlock = CombatTracker.LastBlockTime(self.PlayerId);
            if (myBlock > _lastEngageBlock)
            {
                _lastEngageBlock = myBlock;
                if (CombatTracker.LastBlockAttacker(self.PlayerId) is int a)
                {
                    IPlayer atk = StateTracker.GetPlayerById(a);
                    if (IsCandidate(self, atk, ignoreRange: true))
                    {
                        _engaged = true;
                        _engagedTargetId = a;
                        _targetId = a;
                        return atk;
                    }
                }
            }

            // Nobody has attacked us yet: face and block the nearest player in range, the waiting posture.
            IPlayer near = FindNearestCandidate(self);
            _targetId = near?.PlayerId;
            return near;
        }

        // How long an attacker-lock outlives the strike: long enough to land our riposte (reaction plus window plus a beat).
        private float LockTail => _riposteReactionMax + _riposteWindow + 0.3f;

        // Closest candidate currently mid-strike (winding up or in a committed lethal window) within lock range, or
        // null. Lock range is targetRange, or StrikerLockRange when targeting is unlimited so a distant swing at
        // someone else can't grab us. Once locked, the lock holds on regardless of range (see ResolveTarget).
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
                if (!IsCandidate(self, p, ignoreRange: true)) continue; // team and spawned; range applied below
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

        // A targetable player: spawned and alive (a corpse is skipped), not us, human unless we target bots too,
        // on a hostile faction unless ignoreTeam, and unless ignoreRange within targetRange (<= 0 = unlimited).
        private bool IsCandidate(BotController self, IPlayer p, bool ignoreRange)
        {
            if (p == null || p.PlayerObject == null || !p.IsAlive || p.PlayerId == self.PlayerId) return false;

            if (_ignoreBots && p.IsBot) return false;

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

        private static readonly string[] LeverNames =
        {
            "offensiveRange", "offensiveRangeVariance", "defensiveRange", "defensiveRangeVariance",
            "attackRange", "attackReadBeat", "riposteReactionMin", "riposteReactionMax", "riposteWindow",
            "blockReactionMin", "blockReactionMax", "press", "riposte", "move", "pursue",
            "targetRange", "ignoreTeam", "ignoreBots", "stickyTarget", "engageOnAttack", "passiveRange"
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
                case "ignorebots":   return SetBool(value, v => _ignoreBots = v, "ignoreBots", ref error);
                case "stickytarget": return SetBool(value, v => _stickyTarget = v, "stickyTarget", ref error);
                case "engageonattack": return SetBool(value, v => _engageOnAttack = v, "engageOnAttack", ref error);
                case "passiverange": return SetFloat(value, 0f, v => _passiveRange = v, "passiveRange", ref error);
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
            yield return ("press", _press ? "true" : "false");
            yield return ("riposte", _riposte ? "true" : "false");
            yield return ("move", _move ? "true" : "false");
            yield return ("pursue", _pursue ? "true" : "false");
            yield return ("targetRange", _targetRange.ToString("0.##"));
            yield return ("ignoreTeam", _ignoreTeam ? "true" : "false");
            yield return ("ignoreBots", _ignoreBots ? "true" : "false");
            yield return ("stickyTarget", _stickyTarget ? "true" : "false");
            yield return ("engageOnAttack", _engageOnAttack ? "true" : "false");
            yield return ("passiveRange", _passiveRange.ToString("0.##"));
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
                case "true": case "on": case "1": case "yes": set(true); return true;
                case "false": case "off": case "0": case "no": set(false); return true;
                default: error = $"{name} must be true or false."; return false;
            }
        }

        // Carry the per-bot lever overrides and any pinned target to a Replace replacement, so a bot tuned with
        // 'rc bot cfg' isn't reset to preset defaults on death.
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
            _targetRange = p._targetRange; _ignoreTeam = p._ignoreTeam; _ignoreBots = p._ignoreBots; _stickyTarget = p._stickyTarget;
            _passiveRange = p._passiveRange;
            _engageOnAttack = p._engageOnAttack; // carry the lever; _engaged/_engagedTargetId are not carried, so a replacement starts passive
            _assignedTargetId = p._assignedTargetId;
            RollHoldRange();
        }
    }
}
