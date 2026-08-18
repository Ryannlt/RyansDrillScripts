using System.Collections.Generic;
using HoldfastSharedMethods;
using UnityEngine;

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
    //   GroupEasy / GroupNormal / Group are those plus formation fighting and the drill-station cycle, so a batch
    //     summoned together waits, wakes as one, backs off to re-form, fights, and returns to its post.
    // StabbingDummy is a separate class (MeleeDummy), a static stabber with no perception.
    //
    // This file is the decision: what the bot does each tick. The levers themselves - the fields, the per-preset
    // defaults, and the IConfigurableAi plumbing - are in MeleeAi.Levers.cs, because presets are lever bundles and
    // that bookkeeping grows with every behaviour added here. The strike-mechanic constants below are NOT levers:
    // they encode how the engine plays a stab out, measured in-game, so changing them just breaks the bot.
    //
    // Targeting is lever-driven too (ResolveTarget): targetRange gates who it engages, ignoreTeam and ignoreBots
    // filter by faction and human-vs-bot, and stickyTarget picks holding one foe versus the closest each tick. An
    // automatic attacker-lock keeps it on whoever is mid-strike, and an ITargetableAi pin lets a future supervisor
    // override the choice.
    //
    // Strike quirk: a raw MeleeStrike token latches an auto-cycling attack loop, so a strike is a short held
    // chamber (one MeleeStrike) released by a single ExecuteMeleeWeaponStrike, which also stops the cycle. See
    // StepAttack.
    public partial class MeleeAi : IBotAi, IConfigurableAi, ITargetableAi, IGuardianAi, ISquadMember
    {
        // How hard the push away from other bots counts next to the movement it is blended into.
        private const float SeparationWeight = 1.5f;

        // Reused when gathering neighbour positions for Steering.Separation, so a 20 Hz tick does not allocate.
        private static readonly List<Vector2> _separationNeighbours = new();

        // Strike-mechanic timings, measured from the engine. Not levers.
        private const float WindupSeconds = 0.15f;    // hold the windup this long (one MeleeStrike) before releasing
        // A committed stab occupies the bot about this long before it can throw again, whether it misses or is
        // blocked (both measured near 1.5s from a human spamming attack; the engine's ~0.35s block stun does not
        // shorten it). Throwing the next strike sooner overlaps the still-playing swing. Timed from release.
        private const float MissedStabDuration = 1.5f;
        // Extra time we refuse to block for after throwing first, so the bot backs its own stab instead of
        // flinching into a guard. Deliberately NOT part of how long the blade is treated as live: it is about
        // nerve, not geometry, and folding it in made the aim cap hold for over a second after every first
        // strike.
        private const float FirstStrikeCommitBonus = 0.4f; // extra commit time when we threw first, so we back our stab instead of flinching into a guard
        private const float MinBlockHold = 0.35f;      // keep a raised guard up at least this long so it reads and animates
        private const float AimOffset = 0.3f;          // sideways aim shift while striking to centre a right-hand stab, metres

        // Quiet period after spawning before the bot may throw anything. Striking on the first tick out of the
        // spawn plays the swing wrong - the animation never settles - which a Replace bot hits every time it comes
        // back next to whoever just killed it. Measured by eye rather than probed, so adjust if a fresh bot still
        // swings badly. Not a lever: it describes the engine, like the timings above.
        private const float SpawnStrikeDelay = 1.0f;

        // Blade geometry, and how long we treat the blade as out. Measurements of the weapon, not balance
        // choices, which is why they sit here rather than in the lever set: answering a game patch means
        // re-measuring them, not retuning them.
        //
        // BladeBearing is the game's own number, read out of the baked strike data by MeleeLogger:
        //
        //   MeleeStrikeHigh | pitch=0 flat=0.90 x=0.26 z=0.86 bearing=16.6
        //   MeleeStrikeLow  | pitch=0 flat=0.95 x=0.25 z=0.91 bearing=15.6
        //
        // so the weapon point sits about 16 degrees to the bot's right at the pitch bots hold. This was 25 for a
        // while, fitted to a hit distribution that the clamp's own edge-parking had polluted, and nine degrees of
        // mis-centring is enough to make the clamp fire on one side and miss on the other. The game's figure
        // climbs with pitch (42 degrees at pitch 2), so this is only right while bots aim level. BladeReach is
        // the far end of that same segment.
        private const float BladeReach = 2.05f;   // how far the blade line extends from the body, metres
        private const float BladeBearing = 16f;   // degrees the blade sits off the facing, positive to the bot's right

        // How long after our release the blade is treated as out, and so how long the mate clamp and the release
        // gate hold. Measured in game rather than derived, because the source figures are in the game's frame of
        // reference and this one is in ours.
        //
        // The source reads as 0.70 (initialProcessingDelay 0.28 + duration 0.42) and MeleeLogger's frames put the
        // game's own IsPlayerMeleeAttacking span at about 0.68, both of which agree with each other and are both
        // too short here. The missing ~0.2s is the lag between us commanding the release and the game starting
        // its execution clock. Worth re-measuring if the tick rate or the AI's command path ever changes.
        private const float StrikeCommitWindow = 0.9f;

        // Movement feel. Kept const for now.
        private const float RangeTolerance = 0.3f;     // slop band around the hold range where the bot just stands
        private const float BackoffThrottle = 1.0f;    // back off at full speed so an approaching attacker can't just fill the gap
        private const float MoveChangeDelay = 0.2f;    // reaction beat before adopting a closer hold range; retreating is immediate
        private const float MoveHysteresis = 0.5f;     // ignore range jitter smaller than this when deciding to advance
        private const float StrikerLockRange = 3f;     // attacker-lock only triggers for strikers within this when targetRange is unlimited
        private const float SlotDeadband = 0.15f;      // close enough to the slot to stop rather than shuffle

        // The tuning levers, the per-preset defaults and the IConfigurableAi plumbing all live in
        // MeleeAi.Levers.cs. Everything below is runtime state: what this bot is doing right now.

        // The ward's faction, refreshed once per tick. While guarding, sides are judged from the ward rather than
        // from the bot itself: the game does not always honour the faction we ask spawnSpecific for (a full team
        // or a capped class gets silently substituted), and a guard that reads sides from its own body would then
        // count its ward as the enemy and cut them down. Judging from the ward makes a wrong-team spawn harmless.
        private FactionCountry? _wardFaction;

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

        // Who last hit our guard, tracked for every preset rather than only the engageOnAttack ones. A drill
        // station has to know it was attacked even when its preset has no engage machine of its own, which is
        // what makes 'post' work on a RiposteDummy. Cleared by StandDown, or a station would wake itself again
        // on a stale provocation the moment it got home.
        private int? _provokedBy;
        private float _lastProvokeBlock;
        private bool _stancePending = true;   // issue EnableCombatStance once, on the first spawned tick
        private bool _runPending = true;      // establish the sticky run toggle once, on first engagement
        private string _blockToken;           // the block playerAction we're currently holding (null = not blocking)

        // Attack sequencer state (see StepAttack).
        private enum AttackPhase { None, Chamber }

        // What the bot is doing, as opposed to where its swing is. Keeping the two apart is the point: a stance
        // added here does not have to be crossed with every strike-state flag in Decide.
        private enum Posture
        {
            Waiting,     // engageOnAttack and not yet provoked: face and block, throw nothing
            BackingOff,  // provoked, re-establishing range and formation before it will swing
            Holding,     // set and in range, but the group's engage delay has not run out yet
            Withdrawing, // breaking off from a live enemy: still blocks and counters, but gives ground and won't press
            Fighting
        }
        private AttackPhase _attackPhase;
        private string _attackDir;            // "High" or "Low" for the strike in progress
        private float _attackCooldownUntil;   // realtime before which we won't start another strike
        private float _strikeReadyAt;         // realtime the bot may first swing at all, set on spawn (SpawnStrikeDelay)
        private bool _releasePending;         // a dropped windup still needs releasing, or the engine keeps cycling it
        private float _chamberStartedAt;      // realtime our windup began (for the "I threw first" read)
        private float _executeAt;             // realtime to release the held windup
        private bool _threwFirst;             // this swing out-timed the enemy's, so commit to it harder

        // Stab-priority state: after our guard absorbs an attack we get a brief riposte window (Decide).
        private bool _blockBaselinePending = true; // take the current block history as the baseline on the first tick
        private float _lastConsumedBlock;     // last block we reacted to as defender (dedupe)
        private float _riposteReadyAt;        // realtime before which we hold the guard (reaction beat) before countering
        private float _priorityUntil;         // while now < this: riposte immediately, don't re-block
        private float _strikeCommittedUntil;  // while now < this: don't block (it'd cancel our own swing). Includes the first-strike nerve bonus.
        private float _bladeLiveUntil;        // while now < this: the blade is actually out, so the aim cap and mate clamp apply

        // Diagnostics only, read by MeleeProbe when a bot kills a bot. The aim the swing wanted versus the aim it
        // was allowed, which is what tells a clamp that never engaged from a clamp that engaged and was not enough.
        private float _lastAimDesired;
        private float _lastAimClamped;
        private int _selfId = -1;
        private float _blockStartedAt;        // realtime the current guard went up (for MinBlockHold)
        private float _blockDesiredSince = -1f; // realtime we first wanted this guard (for block reaction; -1 = not)
        private float _blockReadyAt;          // realtime the guard may go up (start plus block reaction beat)
        private float _rerollAt;              // realtime to next re-roll the hold distance

        // How badly this bot is holding the line right now (see slotError / formationLag).
        private bool _wasInSquad;             // formed up last tick, so we can tell when a bout's formation begins
        private Vector2 _slotBias;            // its personal misplacement, fixed for the bout
        private Vector2 _slotSeen;            // the slot position it is working from, which may be out of date
        private bool _slotSeenValid;          // guards against chasing world origin before the first sample
        private float _resampleAt;            // realtime it next looks at where it is actually supposed to be

        public MeleeAi(BotAiEnum aiType)
        {
            _aiType = aiType;
            SeedLevers(aiType);
            RollHoldRange();
        }

        public BotAiEnum AiType => _aiType;

        public BotIntent Decide(BotController self, float deltaTime)
        {
            if (!self.TryGetPose(out BotPose pose))
                return BotIntent.Idle; // not spawned, issue nothing

            _selfId = self.PlayerId;   // StepAttack records its strike against it and has no self of its own

            // Player ids are recycled, so a new bot can be handed an id that already carries block history from
            // whoever held it before. Take whatever is on record as the baseline on the first tick, so only
            // blocks that land from now on count as ours; otherwise a replacement would come back believing it
            // had just blocked, and immediately counter or engage a player it never fought.
            if (_blockBaselinePending)
            {
                _blockBaselinePending = false;
                _lastConsumedBlock = CombatTracker.LastBlockTime(self.PlayerId);
                _lastEngageBlock = _lastConsumedBlock;
                _lastProvokeBlock = _lastConsumedBlock;
            }

            // Note a fresh hit on our guard. Kept outside the engageOnAttack path so every preset reports being
            // attacked, which is the signal a drill station wakes its group from.
            float provokeBlock = CombatTracker.LastBlockTime(self.PlayerId);
            if (provokeBlock > _lastProvokeBlock)
            {
                _lastProvokeBlock = provokeBlock;
                if (CombatTracker.LastBlockAttacker(self.PlayerId) is int attacker)
                    _provokedBy = attacker;
            }

            // A provocation lasts only as long as the player who made it. Without this it names them forever -
            // StandDown is the only other thing that clears it, and a bot with no station never calls it - so the
            // moment that id respawned the formation would re-form and run at whoever now holds it.
            if (_provokedBy is int provoker && !IsCandidate(self, StateTracker.GetPlayerById(provoker), ignoreRange: true))
                _provokedBy = null;

            // Let go of a windup that was dropped rather than thrown. A raw MeleeStrike latches the engine's
            // attack loop and only ExecuteMeleeWeaponStrike or a block stops it, so abandoning a chamber any
            // other way leaves the bot cycling stabs on its own while nothing in here believes it is attacking.
            // The places that drop one - losing the target, being stood down, going back to escorting - have no
            // action channel at the time, so they flag it and the release goes out here.
            if (_releasePending)
            {
                _releasePending = false;
                _attackCooldownUntil = Time.realtimeSinceStartup + MissedStabDuration;
                return new BotIntent { Action = "ExecuteMeleeWeaponStrike", MoveAxis = Vector2.zero };
            }

            // Enter combat stance once so the bot can block and strike. Consumed only once actually spawned, which
            // makes it the moment the bot enters the world and so where the no-swinging-yet window starts.
            if (_stancePending)
            {
                _stancePending = false;
                _strikeReadyAt = Time.realtimeSinceStartup + SpawnStrikeDelay;
                return new BotIntent { Action = "EnableCombatStance" };
            }

            float now = Time.realtimeSinceStartup;

            // Orders from the coordinator for this tick. Resolved before targeting because they also say where to
            // stand while there is nobody to fight at all, which is how a station holds its post.
            SquadSlot slot = default;
            bool hasSlot = (_squad || _post) && SquadCoordinator.TryGetSlot(self.PlayerId, out slot);

            // Standing in a formation, which needs both the lever and someone to form up with. A lone bot has no
            // partner to divide the work with, so it fights as itself even while it holds a slot.
            bool inSquad = _squad && hasSlot && slot.Members > 1;

            // Freshly formed up: pick how badly this bot holds the line for the coming bout. Rolled once and kept,
            // so a player can read a sloppy pair and work the gap instead of watching it wander; rolled from zero,
            // so some bouts they simply line up properly and the gap is not there at all.
            if (inSquad && !_wasInSquad) RollFormationError();
            _wasInSquad = inSquad;

            // A slot is the whole movement decision for a formation. For a lone bot on a station it only governs
            // walking back to the post and backing off when provoked; once the fight is on it keeps its own
            // spacing, so turning post on doesn't quietly turn a duellist into a formation member.
            bool slotDrivesMovement = hasSlot && (inSquad || slot.Phase != SquadPhase.Engaged);

            // The ward is resolved first because targeting reads sides from them while we are guarding.
            IPlayer ward = ResolveWard(self);
            _wardFaction = ward?.Faction;

            IPlayer target = ResolveTarget(self, now);

            // Escorting someone takes precedence over picking a fight: unless the enemy we found is actually a
            // threat to them, hold station at their side instead.
            if (ward != null && !ThreatensWard(ward, target, now))
                return GuardIntent(self, pose, ward);

            if (target?.PlayerObject == null)
            {
                AbandonChamber(); // don't resume a stale chamber when a target is reacquired

                // Nobody to fight. A bot with a slot walks back to it and faces the way the line faces, which is
                // what returns a station to its post once the drill is over rather than leaving it wherever the
                // fight happened to end.
                if (!hasSlot)
                    return DropBlock(new BotIntent { MoveAxis = Vector2.zero });

                // Exact slot, no bias and no lag: holding a fighting line badly is the drill, walking home badly
                // is just sluggish.
                Vector2 idleMove = _move ? EngageVelocity(pose, slot.Position, true, slot.Position, 0f, false) : Vector2.zero;

                // Look where it is going while it is going there, and only take up the formation's bearing once
                // it arrives. Movement is relative to facing, so walking a long way home sideways or backwards is
                // needlessly slow; turning first and running is how a person would cross the same ground.
                float idleHeading = idleMove.sqrMagnitude > 1e-4f
                    ? MovementSolver.HeadingOf(idleMove)
                    : slot.Facing;

                var idleIntent = new BotIntent { MoveAxis = ToAxis(pose, idleMove), LookHeading = idleHeading };

                // Running is a sticky engine toggle established once, and it used to be set only on the paths
                // that lead to a fight. A bot walking back to its post never took any of those, so it made the
                // whole trip at walking pace and only sped up later, the first time something provoked it.
                if (_runPending)
                {
                    idleIntent.Running = true;
                    _runPending = false;
                }

                return DropBlock(idleIntent);
            }

            Vector3 tp = target.PlayerObject.transform.position;
            Vector2 targetPos = new Vector2(tp.x, tp.z);
            CombatTracker.TryGet(target.PlayerId, out CombatTracker.MeleeState enemy);

            // What the bot is doing this tick. Deliberately its own axis, separate from where its swing is
            // (_attackPhase, committed, priority below): the two are independent, and a new stance should be a
            // new case here rather than another '&& !flag' on every clause that already exists.
            // Holding sits on top of Breaking rather than replacing it: backing off already stops the swings, and
            // the delay is a second layer that keeps stopping them once the group is set. Timed from the
            // provocation, so the two run together instead of stacking - see GroupState.ProvokedAt.
            Posture posture =
                hasSlot && slot.Phase == SquadPhase.Breaking    ? Posture.BackingOff  // re-forming, guard up, no swings
                : hasSlot && slot.Phase == SquadPhase.Withdrawing ? Posture.Withdrawing
                : hasSlot && now < slot.AttackAllowedAt         ? Posture.Holding     // set, but not cleared to swing yet
                : _engageOnAttack && !_engaged                  ? Posture.Waiting     // provoked-on-attack, not yet provoked
                : Posture.Fighting;

            // Only a fighting bot presses, counters or chases. A withdrawing one is leaving: it does not swing and
            // it does not go looking for someone else, it just gets home without being cut down. Every posture
            // still blocks, and a chamber already in flight still releases below, since a held strike left alone
            // auto-cycles.
            bool fighting = posture == Posture.Fighting;
            bool press = _press && fighting;
            bool riposte = _riposte && fighting;
            bool pursue = _pursue && fighting;

            // Face the target's actual position; leading where it's going made the bot over-rotate up close. While
            // striking, nudge the aim sideways because the stab comes off the right of the body, so the thrust
            // lands on centre of mass.
            // Blade-live, not commit: the aim offset, the mate clamp and the turn cap all care about whether the
            // weapon is actually out, and _strikeCommittedUntil carries the first-strike nerve bonus on top.
            bool swingLive = _attackPhase == AttackPhase.Chamber || now < _bladeLiveUntil;

            Vector2 aimPos = targetPos;
            if (swingLive)
            {
                Vector2 toTarget = targetPos - pose.Position;
                if (toTarget.sqrMagnitude > 1e-4f)
                {
                    Vector2 dir = toTarget.normalized;
                    Vector2 botLeft = new Vector2(-dir.y, dir.x); // shift aim to the bot's left to centre a right-hand stab
                    aimPos = targetPos + botLeft * AimOffset;
                }
            }

            // A swing follows the aim for its whole length, so tracking the target freely is what drags a stab
            // through a squadmate who has moved into the way since it started. Only the aim is constrained, and
            // only while a swing is live: the stab still flies, it just stops turning, which is what a player
            // does. Outside a swing there is nothing to hit anyone with, so the bot faces freely.
            bool mateAcrossBlade = false;
            float aimHeading = swingLive
                ? ClampAimAroundMates(self, pose, aimPos, out mateAcrossBlade)
                : MovementSolver.HeadingTo(pose.Position, aimPos);

            // How far the commanded heading actually moves this tick, which matters because the game does not
            // sample the blade at our tick rate: it stitches the blade's position between its own frames with
            // rays, so a heading that jumps far enough in one tick sweeps everything in between even though
            // neither end of the jump was over a squadmate. Nothing else limits this - BotController hands
            // LookHeading straight to SetInputRotation - and measured bot turns reach 129 degrees in a tick.
            //
            // There was a swingTurnRate lever here that capped it. It is gone, and deliberately so: a rate
            // ceiling cannot tell turning-to-track from turning-through-a-mate, so it throttled both. Measured in
            // play, 100 deg/s already loses a player circling at 1.5m while anything slow enough to stop the drag
            // is far below that. The directional clamp above is the mechanism that works, because it only
            // restricts turning *toward* a mate. The figure is still reported so a swing trace shows the sweep.
            float turned = swingLive ? Mathf.DeltaAngle(pose.Heading, aimHeading) : 0f;

            if (swingLive && MeleeProbe.IsProbing(self.PlayerId))
                MeleeProbe.LogSwingTick(self.PlayerId, pose.Heading, _lastAimDesired, aimHeading, turned,
                    mateAcrossBlade, _gateRadius, _clampRadius, DescribeMates(self, pose));

            BotIntent intent = new BotIntent { LookHeading = aimHeading };


            // Stab priority: after our guard absorbs the enemy's stab they're recovering and can't beat our
            // counter, so we give ourselves a brief window to riposte at once, ignoring the attack cooldown.
            // Priority comes only from the engine's block event, a real absorbed hit, never a guess.
            bool priority = false;
            if (riposte)
            {
                // A coordinated formation shares its guard: a stab any member turns aside is spent, and its
                // thrower is recovering from it whichever of them caught it, so the whole line is clear to counter
                // together. The checks below still apply, so a fresh chamber aimed at this bot revokes it.
                // Only the cooperative half of the coordinate axis shares. Below neutral the pair is actively
                // avoiding working together, and at neutral they simply are not - neither is a reason to hand a
                // bot its partner's reads, so the chance ramps from nothing at 0.5 to always at 1.
                float myBlock = CombatTracker.LastBlockTime(self.PlayerId);
                float shareChance = Mathf.Max(0f, (_coordinate - 0.5f) * 2f);
                if (inSquad && Random.value < shareChance) myBlock = Mathf.Max(myBlock, slot.BlockTime);

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

                    // Not fighting means the guard goes up on the passive beat, instant by default. A station
                    // exists to be attacked, so its opening block must be dependable, or at easy reaction speeds
                    // a walk-up stab ends the drill before it began. The same holds while withdrawing: a bot
                    // that has stopped swinging and is heading home should not be free to cut down on the way.
                    // Difficulty lives in the fight itself.
                    _blockReadyAt = now + (fighting
                        ? Random.Range(_blockReactionMin, _blockReactionMax)
                        : _passiveBlockReaction);
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

            // Last resort. A mate is already across the blade, so no amount of aim clamping keeps the rays off
            // them this tick. Raising a guard cancels our own strike outright: the game checks
            // PlayerBase.State.ContainsMeleeBlock() every frame and marks the strike markedForDeletion the moment
            // it is true. That is the same mechanism _strikeCommittedUntil exists to avoid tripping by accident,
            // spent deliberately here because running a squadmate through is worse than a stab that never lands.
            // committed, not swingLive: during the chamber there is no blade out yet, so clamping the aim is
            // enough and cancelling the windup would cost a stab for nothing.
            // Blade-live rather than committed: there is nothing to cancel once the weapon is back in.
            if (_abortOnMate && mateAcrossBlade && now < _bladeLiveUntil && desiredBlock == null)
            {
                desiredBlock = "MeleeBlockHigh";
                if (MeleeProbe.IsProbing(self.PlayerId))
                    Logger.Log($"MeleeSwing[{self.PlayerId}]: aborting own stab, mate across the blade.", LogLevel.INFO);
            }

            Vector2 worldMove = Vector2.zero;

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
                if (_move)
                    worldMove = EngageVelocity(pose, targetPos, slotDrivesMovement, SlotTarget(slot, now), posture == Posture.Waiting ? _passiveRange : MovementRange(false, now), pursue);
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
                if (_move)
                    worldMove = EngageVelocity(pose, targetPos, slotDrivesMovement, SlotTarget(slot, now), posture == Posture.Waiting ? _passiveRange : MovementRange(press, now), pursue);

                // Attack when the enemy isn't threatening. With priority this is the post-block riposte and fires
                // immediately (ignoring cooldown and press), otherwise it's throwing first, gated by press. Skip
                // the tick we drop the block (StopMeleeBlock took the action channel; the strike resumes next tick).
                // Also call while a chamber is in progress even if press/riposte just went off (e.g. a Dueling bot
                // whose target died mid-swing drops to passive) so the held MeleeStrike is released cleanly instead
                // of being left to auto-cycle.
                if ((press || riposte || _attackPhase == AttackPhase.Chamber) && !droppedBlock)
                    // Lane discipline follows the formation, not the coordination: a duellist should still hold a
                    // swing that would go through the bot beside it. The direction the line wants is only a
                    // suggestion here - StepAttack decides whether to take it, once, as the swing begins.
                    StepAttack(ref intent, pose, targetPos, priority, press,
                        (!inSquad || slot.LaneClear) && !(_gateOnMate && MateInBladeBand(self, pose)),
                        inSquad ? self.GroupId : 0,
                        inSquad ? (slot.StabHigh ? "High" : "Low") : null,
                        inSquad ? (slot.SharedHigh ? "High" : "Low") : null);
            }

            // While a slot is driving the movement it is the whole decision: the formation already guarantees the
            // spacing, so blending separation into it can only pull against the slot and slow the bot down.
            if (!slotDrivesMovement)
                worldMove = WithSeparation(self, pose, worldMove);

            intent.MoveAxis = ToAxis(pose, worldMove);

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
        // fires when press is enabled. assignedDir is the direction that makes an updown with the neighbour and
        // matchDir the one that deliberately avoids it; both null when there is no formation. Which is taken, if
        // either, is decided by PickStabDirection.
        private void StepAttack(ref BotIntent intent, BotPose pose, Vector2 targetPos, bool priority, bool press, bool laneClear, int groupId, string assignedDir, string matchDir)
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

                    MeleeProbe.NoteStrike(_selfId, now, _lastAimDesired, _lastAimClamped, _targetId ?? -1, laneClear);
                    _priorityUntil = 0f;                              // riposte thrown, priority spent
                    // Commit to the swing, since blocking now would cancel it. If we threw first, commit longer so
                    // the bot backs its own stab as it lands instead of flinching into a guard and eating the trade.
                    _bladeLiveUntil = now + StrikeCommitWindow;
                    _strikeCommittedUntil = _bladeLiveUntil + (_threwFirst ? FirstStrikeCommitBonus : 0f);
                }
                return;
            }

            // Fresh out of the spawn: don't swing yet. Checked after the chamber release above so nothing can be
            // left held, and ahead of the priority riposte because a counter is just as capable of firing on the
            // first tick and playing the animation wrong.
            if (now < _strikeReadyAt) return;

            // Don't start a swing that would go through a squadmate. Only the start is gated: a chamber already in
            // flight still releases above, because a held MeleeStrike left alone auto-cycles. Holding the chamber
            // until the lane clears, and feinting out of it if it doesn't, is the next milestone.
            if (!laneClear) return;

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
            // Bayonet is High/Low, and coordinate is an axis rather than a switch: 1 always throws opposite to
            // the neighbour, 0 always throws the same as it, 0.5 leaves it to chance. Both ends are deliberate
            // behaviour, which is why the scale needs a middle - two bots picking freely already land an updown
            // half the time, so "never updown" is something a pair has to actively agree on, not the absence of
            // agreement. Decided once, here, as the swing starts; rolling per tick would flicker it mid-windup.
            string dir = PickStabDirection(assignedDir, matchDir);

            // Last gate before the swing, so only a bot that is actually about to stab files a claim. Priority
            // ripostes skip the press, cooldown and range checks above but still arrive here, which is the point:
            // two bots countering the same blocked swing is the usual way a player eats an unblockable pair.
            // Denied just means not yet, and next tick re-rolls the direction, which below coordinate 1 may come
            // up matching and be let straight through.
            if (!SquadCoordinator.TryClaimStab(groupId, now, _stabSeparation, dir == "High")) return;

            _attackDir = dir;
            _chamberStartedAt = now;
            _executeAt = now + WindupSeconds;
            _threwFirst = false;                               // set true only if we out-time the enemy this windup
            _attackPhase = AttackPhase.Chamber;
            intent.Action = "MeleeStrike" + _attackDir;
        }

        // Drop a windup for a reason other than throwing or blocking it: the target is gone, the group has been
        // stood down, the bot is back to escorting. Forgetting the chamber is not enough - the engine is still
        // cycling the attack until something ends it - so the release is queued for the next tick, when there is
        // an action channel to send it on. Blocking is the one case that does NOT come through here, because a
        // block cancels the windup by itself.
        private void AbandonChamber()
        {
            if (_attackPhase == AttackPhase.Chamber) _releasePending = true;
            _attackPhase = AttackPhase.None;
        }

        // Which way to swing, given what the formation would like. coordinate runs from 0 to 1 with chance in the
        // middle: the top half is the odds of deliberately making an updown, the bottom half the odds of
        // deliberately refusing one, and anything not decided that way is a free pick.
        //
        // Chance has to sit at 0.5 rather than at 0, because a free pick is not neutral - two bots choosing
        // independently still throw opposite half the time. Refusing to updown is its own agreement and needs its
        // own end of the scale, which is the whole reason this is an axis and not a probability of cooperating.
        private string PickStabDirection(string assignedDir, string matchDir)
        {
            if (assignedDir != null && matchDir != null)
            {
                float roll = Random.value;

                if (_coordinate > 0.5f && roll < (_coordinate - 0.5f) * 2f) return assignedDir;
                if (_coordinate < 0.5f && roll < (0.5f - _coordinate) * 2f) return matchDir;
            }

            return Random.value < 0.5f ? "High" : "Low";
        }

        // Picks how badly this bot holds its place for the coming bout. Both magnitudes are rolled from zero, so
        // the tier decides how often a bot is wrong rather than how wrong it always is - some bouts it simply
        // stands correctly and the gap a player was expecting is not there.
        private void RollFormationError()
        {
            float drift = Random.Range(0f, _slotError);
            float angle = Random.Range(0f, Mathf.PI * 2f);

            // Held in the line's own frame - x across it, y along it toward the enemy - so the sideways half can
            // be limited on its own. Pushed more than half a spacing sideways a bot ends up past its neighbour,
            // the sort that keeps each one on its own side swaps them, and the pair walks through each other
            // trading slots every tick. Fore and aft needs no such limit: a staggered line, one bot up and one
            // hanging back, is exactly the badly-held formation this is here to produce.
            float across = Mathf.Cos(angle) * drift;
            float along = Mathf.Sin(angle) * drift;
            float acrossLimit = _squadSpacing * 0.45f;

            _slotBias = new Vector2(Mathf.Clamp(across, -acrossLimit, acrossLimit), along);

            _slotSeenValid = false;   // start the bout looking at where it really should be
            _resampleAt = 0f;
        }

        // Where the bot believes its slot is: out of date by up to formationLag, and off by its bias for this
        // bout. Everything that moves it toward a slot goes through here, so imperfection lands in one place.
        private Vector2 SlotTarget(SquadSlot slot, float now)
        {
            if (!_slotSeenValid || now >= _resampleAt)
            {
                _slotSeen = slot.Position;
                _slotSeenValid = true;

                // Rolled from zero each time, so a laggy bot is sometimes barely late and sometimes badly so,
                // rather than reliably a fixed beat behind.
                _resampleAt = now + Random.Range(0f, _formationLag);
            }

            // The bias is in the line's frame, so it turns with the formation: a bot that hangs back stays behind
            // the line as the pair rotates, rather than its error swinging round to the flank.
            Vector2 forward = MovementSolver.DirectionFromHeading(slot.Facing);
            Vector2 across = new Vector2(-forward.y, forward.x);

            return _slotSeen + across * _slotBias.x + forward * _slotBias.y;
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

        // World movement to sit at the given hold range: close in if too far, ease back if too close, stand still
        // inside the tolerance band. Re-solved each tick against the live pose since the target moves. With pursue
        // off we don't close a gap that's too big (only hold or back off), so a retreating player can walk away
        // and disengage instead of being followed.
        private static Vector2 HoldRangeVelocity(BotPose pose, Vector2 targetPos, float range, bool pursue)
        {
            Vector2 toTarget = targetPos - pose.Position;
            float dist = toTarget.magnitude;
            if (dist < 1e-4f) return Vector2.zero;

            Vector2 dir = toTarget / dist;
            if (dist > range + RangeTolerance) return pursue ? dir : Vector2.zero;
            if (dist < range - RangeTolerance) return -dir * BackoffThrottle;
            return Vector2.zero;
        }

        // Movement for this tick. While a slot is driving (a formation, or a station walking home or backing off)
        // it is the whole decision, taken at full throttle: Arrive was used here first and its ramp is why bots
        // crawled, since a slot under a metre away sits deep inside the slow radius and comes out at a fraction of
        // speed. A small deadband stops a bot that is already on its mark from shuffling.
        private static Vector2 EngageVelocity(BotPose pose, Vector2 targetPos, bool useSlot, Vector2 slotPos, float holdRange, bool pursue)
        {
            if (useSlot)
            {
                return (slotPos - pose.Position).sqrMagnitude < SlotDeadband * SlotDeadband
                    ? Vector2.zero
                    : Steering.Seek(pose, slotPos);
            }

            return HoldRangeVelocity(pose, targetPos, holdRange, pursue);
        }

        // Adds the push away from nearby bots. It applies even when the bot would otherwise stand still, which is
        // the point: a clump that never spreads out spends the fight swinging through each other.
        private Vector2 WithSeparation(BotController self, BotPose pose, Vector2 worldMove)
        {
            if (_separationRange > 0f)
                worldMove += Separation(self, pose) * SeparationWeight;

            return worldMove;
        }

        // Expresses a world movement in the bot's own frame for SetInputAxis.
        private static Vector2 ToAxis(BotPose pose, Vector2 worldMove)
        {
            float magnitude = worldMove.magnitude;
            if (magnitude < 1e-4f) return Vector2.zero;

            return MovementSolver.ToLocalAxis(pose, worldMove / magnitude, Mathf.Min(magnitude, 1f));
        }

        // Repulsion from the other tracked bots, reusing the steering layer's comfort-zone falloff. Only bots are
        // considered: crowding a human is the player's business, but bots stacking on each other is ours.
        // The aim heading, kept from sweeping the blade through a friendly bot.
        //
        // The game's hit test is a set of rays, not a bubble. CastMeleeStrikeRaycasts casts from behind the body
        // out to the weapon, then 1.14m further along the blade, and - the part that matters here - it also casts
        // from where each blade point sat last frame to where it sits now. A blade swinging sideways therefore
        // connects through the gap between frames, and the faster the turn the more ground those rays cover. A
        // mate only has to be *missed*, not cleared by a wide margin, which is why a formation can hold at
        // squadSpacing 0.9 the way real players do.
        //
        // So each friendly is a forbidden angular band of half-width asin(clampRadius / distance), and this limits
        // how far we may turn toward one rather than only checking where the aim ends up: the arc between this
        // heading and the next is exactly what the game's inter-frame rays sweep.
        //
        // Only the aim moves. The swing is never delayed or abandoned here, so a bot that cannot turn far enough
        // simply misses, which is the right outcome and the same one a player accepts. mateAcross reports the one
        // case that leaves: a mate already across the blade, where no clamp helps and only a block can save them.
        private float ClampAimAroundMates(BotController self, BotPose pose, Vector2 aimPos, out bool mateAcross)
        {
            mateAcross = false;

            float desired = MovementSolver.HeadingTo(pose.Position, aimPos);
            _lastAimDesired = desired;

            if (_clampRadius <= 0f) { _lastAimClamped = desired; return desired; }

            // Measured from where the blade points right now, because that is where the sweep starts.
            //
            // The blade is not on the centreline. A bayonet thrust comes off the right of the body, which is why
            // AimOffset already shifts the aim left to centre it on a target, so the danger band has to be
            // centred on the blade rather than on the facing. Without this the clamp is too tight on the side the
            // blade is away from and too loose on the side it is on, which reads in play as bailing early turning
            // one way and still running a mate through turning the other.
            float current = pose.Heading;
            float blade = current + BladeBearing;
            float sweep = Mathf.DeltaAngle(current, desired);
            float limit = sweep;


            FactionCountry? ours = self.Bot.Faction;
            var bots = BotManager.Bots;

            for (int i = 0; i < bots.Count; i++)
            {
                BotController mate = bots[i];
                if (mate.PlayerId == self.PlayerId) continue;

                // Enemies never constrain a swing. Unknown factions are treated as friendly, so a bot that has
                // not spawned yet cannot be run through by accident.
                FactionCountry? theirs = mate.Bot.Faction;
                if (ours != null && theirs != null && theirs != ours) continue;

                if (!(mate.Position is Vector3 p)) continue;

                Vector2 toMate = new Vector2(p.x, p.z) - pose.Position;
                float dist = toMate.magnitude;

                // Only the blade's own length matters. There is deliberately no "further away than the target"
                // skip: the strike does not stop at the first body it meets, it gathers everyone its rays touch
                // and spends its single hit on whoever is nearest, so a mate standing behind the target is still
                // at risk the moment the target steps aside.
                if (dist < 1e-4f || dist > BladeReach) continue;

                float halfAngle = Mathf.Asin(Mathf.Clamp01(_clampRadius / dist)) * Mathf.Rad2Deg;

                // Off the blade, not off the facing. limit stays a delta on the heading either way, since the
                // blade travels with the body and the offset between them is fixed.
                float mateDelta = Mathf.DeltaAngle(blade, MovementSolver.HeadingOf(toMate));

                // Crowded mates widen the band toward the whole forward arc, because bearing stops predicting
                // anything at close range: the band is an angle from the chest while the blade is a segment
                // swinging 0.9 to 2.0m out, so rotating at all drags the arc through someone standing right
                // there. Only ahead of the blade - a mate behind cannot be reached by a thrust, and treating
                // them as blocking would pin the bot facing backwards.
                //
                // Ramped in over the closing distance rather than switched on at the threshold. As a hard switch
                // this stepped the band from about 30 degrees to 90 the instant a mate crossed the line, and a
                // partner shuffling either side of it moved the clamp target by tens of degrees from one tick to
                // the next, which is the coarse mid-stab turning that the smooth clamp otherwise removed.
                if (_mateCrowdDistance > 0f && dist < _mateCrowdDistance && Mathf.Abs(mateDelta) < 90f)
                {
                    float crowd = Mathf.InverseLerp(_mateCrowdDistance, _mateCrowdDistance * 0.6f, dist);
                    halfAngle = Mathf.Lerp(halfAngle, 90f, crowd);
                }

                // Already pointing through them. Steer for the nearest way out rather than giving up: this used
                // to 'continue', which left the bot turning completely freely with a mate under its blade and
                // nothing but the abort to fall back on. That is what made the bot look like it never
                // anticipated anything and merely decided whether to let the stab fly.
                //
                // It will often not finish in time - clearing the band takes several ticks against a strike that
                // is live for about 0.4s - which is why the release gate and the turn cap matter more than this.
                if (Mathf.Abs(mateDelta) < halfAngle) mateAcross = true;

                // One rule, whether the mate is inside the band or outside it: stay on the side of them we are
                // already on, no closer than the band's edge.
                //
                // This used to be two branches - slide toward the edge when outside, jump to the far side when
                // inside - which computed the same target but applied it differently, leaving a step at the
                // boundary. A mate drifting across it flipped the commanded turn between 0 and tens of degrees
                // every tick, which is the mid-stab oscillation. Written as one clamp it is continuous: the
                // limit eases to zero as the mate approaches and goes negative as they cross, with no jump.
                //
                // No test on the direction of the intended turn either. Position alone decides, so turning away
                // from a mate is never restricted and the two sides behave symmetrically.
                float edge = halfAngle + _bladeMargin;

                if (mateDelta > 0f) limit = Mathf.Min(limit, mateDelta - edge);
                else                limit = Mathf.Max(limit, mateDelta + edge);
            }

            float heading = current + limit;
            _lastAimClamped = heading;
            return heading;
        }

        // Whether a squadmate currently stands in the blade's band, using the same geometry the clamp uses so the
        // two can never disagree about what counts as dangerous.
        //
        // This gates the *start* of a swing. It is the only mechanism that can reliably help, because once a stab
        // is live the bot has about 0.4s and a measured median of 3 degrees per tick to turn with, which is not
        // enough to clear a band this wide. A bot that cannot throw safely holds its stab and keeps its guard up,
        // which is what a player does.
        private bool MateInBladeBand(BotController self, BotPose pose)
        {
            if (_gateRadius <= 0f) return false;

            float blade = pose.Heading + BladeBearing;
            FactionCountry? ours = self.Bot.Faction;
            var bots = BotManager.Bots;

            for (int i = 0; i < bots.Count; i++)
            {
                BotController mate = bots[i];
                if (mate.PlayerId == self.PlayerId) continue;

                FactionCountry? theirs = mate.Bot.Faction;
                if (ours != null && theirs != null && theirs != ours) continue;

                if (!(mate.Position is Vector3 p)) continue;

                Vector2 toMate = new Vector2(p.x, p.z) - pose.Position;
                float dist = toMate.magnitude;
                if (dist < 1e-4f || dist > BladeReach) continue;

                // Deliberately narrower than the clamp, and with no crowding rule. Refusing to throw is the only
                // thing here that costs a stab, so it stays tight and lets the clamp carry the safety: a crowded
                // mate stops the bot turning into them rather than stopping the swing outright. Widening this is
                // the lever to reach for if bots still kill through a stab they should never have started.
                float halfAngle = Mathf.Asin(Mathf.Clamp01(_gateRadius / dist)) * Mathf.Rad2Deg + _bladeMargin;

                if (Mathf.Abs(Mathf.DeltaAngle(blade, MovementSolver.HeadingOf(toMate))) < halfAngle) return true;
            }

            return false;
        }

        // One line per tick while a swing is live, for the probe. This exists because the FriendlyFire line
        // records the clamp only at release, so it never showed what the clamp did *during* a swing, which is the
        // only time it does anything at all.
        private string DescribeMates(BotController self, BotPose pose)
        {
            System.Text.StringBuilder sb = new System.Text.StringBuilder();
            FactionCountry? ours = self.Bot.Faction;
            var bots = BotManager.Bots;

            for (int i = 0; i < bots.Count; i++)
            {
                BotController mate = bots[i];
                if (mate.PlayerId == self.PlayerId) continue;

                FactionCountry? theirs = mate.Bot.Faction;
                if (ours != null && theirs != null && theirs != ours) continue;
                if (!(mate.Position is Vector3 p)) continue;

                Vector2 toMate = new Vector2(p.x, p.z) - pose.Position;
                float dist = toMate.magnitude;
                if (dist < 1e-4f) continue;

                sb.Append(" | mate=").Append(mate.PlayerId)
                  .Append(" d=").Append(dist.ToString("0.00"))
                  // Off the blade, matching what the clamp compares, so a held swing and its reason line up.
                  .Append(" off=").Append(Mathf.DeltaAngle(pose.Heading + BladeBearing, MovementSolver.HeadingOf(toMate)).ToString("0.#"))
                  .Append(" half=").Append((Mathf.Asin(Mathf.Clamp01(_clampRadius / dist)) * Mathf.Rad2Deg).ToString("0.#"))
                  .Append(dist > BladeReach ? " outOfReach" : "");
            }

            return sb.ToString();
        }

        private Vector2 Separation(BotController self, BotPose pose)
        {
            _separationNeighbours.Clear();

            var bots = BotManager.Bots;
            for (int i = 0; i < bots.Count; i++)
            {
                if (bots[i].PlayerId == self.PlayerId) continue;
                if (bots[i].Position is Vector3 p)
                    _separationNeighbours.Add(new Vector2(p.x, p.z));
            }

            return Steering.Separation(pose, _separationNeighbours, _separationRange);
        }

        // The friendly this bot escorts, or null when it isn't guarding anyone or that player is gone. A ward who
        // dies or leaves simply releases the bot back to ordinary melee behaviour rather than freezing it.
        private IPlayer ResolveWard(BotController self)
        {
            if (!_guard || !(_guardTargetId is int wardId)) return null;

            IPlayer ward = StateTracker.GetPlayerById(wardId);
            if (ward == null || ward.PlayerObject == null || !ward.IsAlive || wardId == self.PlayerId) return null;

            return ward;
        }

        // Whether the escort should break off and fight: an enemy has closed to within guardRange of the ward, or
        // the ward is swinging, which means they are already in a melee we should be part of.
        private bool ThreatensWard(IPlayer ward, IPlayer target, float now)
        {
            if (CombatTracker.TryGet(ward.PlayerId, out CombatTracker.MeleeState wardState) && wardState.IsThreat(now))
                return true;

            if (target?.PlayerObject == null) return false;

            Vector3 wardPos = ward.PlayerObject.transform.position;
            return (target.PlayerObject.transform.position - wardPos).sqrMagnitude <= _guardRange * _guardRange;
        }

        // Escort posture: hold station near the ward, guard down, no attacks, facing the way they face so the bot
        // reads as part of their line rather than staring at them.
        private BotIntent GuardIntent(BotController self, BotPose pose, IPlayer ward)
        {
            AbandonChamber(); // don't carry a chamber into the lull

            Transform wardTransform = ward.PlayerObject.transform;
            Vector2 wardPos = new Vector2(wardTransform.position.x, wardTransform.position.z);

            var intent = new BotIntent
            {
                LookHeading = wardTransform.eulerAngles.y,
                MoveAxis = ToAxis(pose, WithSeparation(self, pose, HoldRangeVelocity(pose, wardPos, _guardFollowRange, pursue: true))),
            };

            if (_runPending)
            {
                intent.Running = true;
                _runPending = false;
            }

            return DropBlock(intent);
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
                if (IsCandidate(self, p, ignoreRange: true))
                {
                    _targetId = pinned;   // report the pin too, or CurrentTargetId contradicts who we are fighting
                    return p;
                }
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

            // Never raise a hand to the player we are guarding, whatever faction this body ended up on. Only
            // while actually guarding: otherwise a bot summoned for sparring would refuse to fight its summoner.
            if (_guard && _guardTargetId is int wardId && p.PlayerId == wardId) return false;

            if (!_ignoreTeam)
            {
                // Sides are judged from the ward while guarding, otherwise from our own body.
                FactionCountry? ourSide = _wardFaction ?? self.Bot.Faction;
                if (!p.Faction.HasValue || !ourSide.HasValue || p.Faction.Value == ourSide.Value)
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

        // Set by the summon commands so a bot summoned onto a player escorts that player. Equivalent to setting
        // the guardTarget lever, which is the manual route for a squad that already exists.
        public void SetGuardTarget(int playerId) => _guardTargetId = playerId > 0 ? playerId : (int?)null;

        // Whoever this bot settled on last tick. Groups are formed from the spawn batch, not from this, but a
        // group with no station of its own adopts it so the formation orients on what its members are fighting.
        public int? CurrentTargetId => _targetId;

        public bool WantsSquad => _squad;

        public SquadSettings SquadSettings => new SquadSettings
        {
            Spacing = _squadSpacing,
            LaneHalfWidth = _laneHalfWidth,
            Standoff = _squadStandoff,
            Post = _post,
            Breakoff = _breakoff,
            BreakoffRange = _breakoffRange,
            EngageDelay = _engageDelay,
            ResetRange = _resetRange,
            MinMembers = _minMembers,
            ReturnDelay = _returnDelay,
        };

        // Read by BotManager when this bot dies, to decide whether its replacement waits for the bout to finish.
        public bool HoldReplacement => _holdReplacement;

        // Only a real provocation counts, never merely having someone to look at: a waiting bot faces and blocks
        // the nearest player, so reporting that would wake a whole station at anyone who walked past. Presets with
        // engageOnAttack have already resolved this into an engagement; the rest fall back to the raw block, which
        // is the same signal ResolveEngageOnAttack reads.
        public int? ProvokedBy => _engaged ? _engagedTargetId : _provokedBy;

        // Woken by a squadmate being provoked. This is the same state a bot reaches by being stabbed itself, so
        // the rest of Decide cannot tell the difference and the group fights as one.
        public void Engage(int playerId)
        {
            _engaged = true;
            _engagedTargetId = playerId;
            _targetId = playerId;
        }

        // Back to waiting. The chamber goes with it, so a stab held when the fight ended is not still there to be
        // released at whoever walks up next.
        public void StandDown()
        {
            _engaged = false;
            _engagedTargetId = null;
            _provokedBy = null;   // spent; leaving it set would re-wake the station the instant it got home
            AbandonChamber();
        }

        // Carry the per-bot lever overrides and any pinned target to a Replace replacement, so a bot tuned with
        // 'rc bot cfg' isn't reset to preset defaults on death.
        public void InheritFrom(IBotAi previous)
        {
            if (!(previous is MeleeAi p)) return;

            CopyLeversFrom(p);                       // every lever, including guardTarget (see MeleeAi.Levers.cs)
            _assignedTargetId = p._assignedTargetId;  // a standing order to fight someone outlives the bot

            // Deliberately NOT carried: _engaged / _engagedTargetId / _provokedBy. Being provoked is something a
            // bot earns, so a replacement starts passive rather than resuming a fight it was never in. A station
            // still pulls it straight back in, because SquadCoordinator re-asserts the group's target every tick.
            RollHoldRange();
        }
    }
}
