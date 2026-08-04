using System.Collections.Generic;
using UnityEngine;

namespace MDS.Systems
{
    // Where a squad member should stand and whether it may swing. Produced once per tick by SquadCoordinator and
    // read by the AI; a bot with no entry is fighting alone and keeps its ordinary duelling behaviour.
    public struct SquadSlot
    {
        public Vector2 Position;  // world XZ the bot should hold
        public bool LaneClear;    // no friendly between this bot and the target, so a stab is safe to throw
        public int Members;       // how many bots share this group, so the AI can tell a duel from a gang-up
        public float BlockTime;   // the most recent guard any member got in, shared so the whole squad can counter
        public bool StabHigh;     // the direction to swing, alternated along the line so neighbours never match
        public bool SharedHigh;   // the one direction the whole line would throw, for a pair deliberately matching
        public SquadPhase Phase;  // waiting, backing off, or fighting
        public float Facing;      // heading the line faces, used while there is no enemy to look at
    }

    // Turns a group of bots into a formation, which is what stops them piling onto one spot and cutting each
    // other down.
    //
    // Membership is the spawn batch: bots summoned by one command are one group for life, carried as
    // BotController.GroupId. It has to work that way because a group must exist before the fight does. Bucketing
    // by shared target, which is what this did originally, cannot describe bots standing idle waiting to be
    // provoked, and it cannot propagate a provocation either, since being stabbed is a per-bot event.
    //
    // The formation is deliberately simple. A group holds an anchor, and members stand on a circle centred on it
    // whose diameter is the spacing, so a pair sits on opposite ends of one diameter. The diameter is kept
    // perpendicular to the anchor-to-enemy line, so the enemy always faces the gap between the two and is
    // contained between them. When the enemy runs around the anchor the pair rotates around it, one moving back
    // while the other moves forward. They never chase: wherever the enemy goes, the formation turns in place.
    //
    // An earlier version built the frame from the members' own mean position, which moved as they moved and left
    // the formation chasing itself. The fixed anchor is what makes the rotation predictable.
    //
    // A group with Post set is a drill station and runs a three-phase cycle: it waits at its post, backs off to
    // re-form when someone provokes any member, fights, and then returns to the post to re-arm for the next
    // player. Backing off before fighting exists because the out-of-range dance for stab priority is most of the
    // skill, and starting from the activation stab would skip it.
    //
    // Station and formation are separate. A batch of one is a legitimate station: it waits, backs off, fights as
    // itself, and walks home, which is what makes the post levers worth having on a plain duellist. Only the
    // formation half needs company, and the AI is what decides it has any (see MeleeAi's slot handling).
    //
    // This generalises to Xv1 unchanged: more members simply means more offsets along the same line.
    public static class SquadCoordinator
    {
        private static readonly Dictionary<int, SquadSlot> _slots = new();
        private static readonly Dictionary<int, List<BotController>> _byGroup = new();
        private static readonly Dictionary<int, GroupState> _groups = new();
        private static readonly List<int> _finishedGroups = new();

        // The formations, keyed by the batch whose fight state the line is built from, and the batch -> that key
        // map. How long an association lasts depends on whether the members are a team: a coordinated group stays
        // joined until it goes home, so bots a player gathered by walking them together are as real a group as a
        // summoned line and wake, withdraw and reset as one. An uncoordinated formation - duellists who merely
        // stopped getting in each other's way - breaks up the moment the bout ends and has to be provoked into
        // existence again.
        private static readonly Dictionary<int, List<BotController>> _formations = new();
        private static readonly Dictionary<int, int> _memberOf = new();
        private static readonly List<int> _disbanding = new();

        // Kills waiting to be turned into a wake, groupId -> killer. Filled from the kill callback, which fires
        // outside the tick, and consumed in StepPhase so every phase change still happens in one place.
        private static readonly Dictionary<int, int> _pendingWakes = new();

        // Slack either side of the standoff. An enemy circling inside this band leaves the anchor alone, which is
        // what keeps a rotation from turning into a chase.
        private const float AnchorTolerance = 0.4f;

        // How fast the anchor may reposition, metres per second. Without a cap it would land on the standoff band
        // the instant the enemy moved and the members would spend their lives chasing a slot that keeps jumping
        // ahead of them. The value that works is set by how fast a bot can actually run, which the engine fixes,
        // so it is measured rather than tuned.
        private const float FollowSpeed = 3f;

        // How close to its slot a member counts as being in place, used to decide the formation has re-formed.
        private const float SettleRadius = 0.7f;

        // Backing off gives up after this long. A player who simply walks into a bot can stop it reaching its
        // slot, and without a cap the group would stand there refusing to fight for as long as they kept it up.
        private const float BreakoffTimeout = 5f;

        // A withdrawal gives up after this long. Without it a player who simply follows a retreating group could
        // hold it in the withdrawing state indefinitely and keep the station from re-arming.
        private const float WithdrawTimeout = 10f;

        // Per-group state. Anchor and phase both outlive any individual member, which is what lets a station
        // survive its bots being killed and replaced.
        private class GroupState
        {
            public Vector2 Post;         // where the batch was stood up; its bots go home here
            public float PostHeading;    // the way it faced when stood up
            public float RestHeading;    // the way it is facing where it currently rests, if that isn't the post
            public int FoundedCount;     // members the batch was summoned with; also its floor for minMembers

            // Fight state. Held by whichever batch is leading the formation, which is the batch itself whenever
            // it is standing alone.
            public Vector2 Anchor;
            public SquadPhase Phase;
            public int? TargetId;
            public float PhaseSince;
            public int Strength;         // biggest the formation has been this time out, for groups gathered from batches

            // The direction a deliberately-matching line throws this bout. Rolled once when the bout starts, so
            // a pair refusing to updown is consistent long enough to be read and then different next time -
            // fixed forever would just mean always blocking high.
            public bool SharedStabHigh;
        }

        // The slot for a bot, or false when it isn't part of a squad this tick.
        public static bool TryGetSlot(int playerId, out SquadSlot slot) => _slots.TryGetValue(playerId, out slot);

        // Rebuilds every group's slots. Called once per bot tick, before the bots themselves tick, so each one
        // reads a slot computed from this tick's positions.
        public static void Refresh(IReadOnlyList<BotController> bots, float deltaTime)
        {
            _slots.Clear();
            Bucket(bots);

            // Every batch keeps its own post. That is where its bots came from and where they go home to,
            // whoever they end up fighting beside in between.
            foreach (var group in _byGroup)
                if (group.Value.Count > 0) StateFor(group.Key, group.Value);

            // Batches fighting the same enemy stand as one formation, and stay one until it goes home. The
            // formation - not the batch - is what waits, wakes, fights, withdraws and resets, so walking loose
            // bots onto the same player builds a group as real as one summoned in a line.
            BuildFormations();

            foreach (var formation in _formations)
            {
                List<BotController> members = formation.Value;
                if (members.Count == 0) continue;

                SquadSettings settings = SettingsOf(members[0]);
                GroupState state = _groups[formation.Key];

                // Judged on how big the formation has actually been, not on how the leading batch was summoned.
                // Three bots gathered onto one player are a trio for minMembers, the same as a summoned three.
                state.Strength = Mathf.Max(state.Strength, members.Count);

                StepPhase(formation.Key, state, members, settings);

                IPlayer target = state.TargetId is int id ? StateTracker.GetPlayerById(id) : null;
                bool haveTarget = IsLiveTarget(target);

                if (haveTarget)
                {
                    Vector3 t = target.PlayerObject.transform.position;
                    Vector2 targetPos = new Vector2(t.x, t.z);

                    // A withdrawing group re-forms exactly where the bout ended: the anchor is frozen, not driven
                    // to any range. Backing off first looks reasonable in isolation but the ground is never given
                    // back - the group settles there, waits there, and the next bout starts from there - so every
                    // win walked the drill a few metres further away and the player could not hold a spot to
                    // fight on. Standing still also gets them re-armed sooner, which is the point of not leaving.
                    Vector2 anchor;
                    if (state.Phase == SquadPhase.Withdrawing)
                    {
                        anchor = state.Anchor;
                    }
                    else
                    {
                        float standoff = state.Phase == SquadPhase.Engaged ? settings.Standoff : settings.BreakoffRange;
                        anchor = MoveAnchor(state, targetPos, standoff, deltaTime);
                    }

                    Vector2 toTarget = targetPos - anchor;
                    Vector2 forward = toTarget.sqrMagnitude > 1e-4f ? toTarget.normalized : MovementSolver.DirectionFromHeading(state.PostHeading);
                    AssignSlots(members, targetPos, anchor, forward, state, settings);

                    // Settling is judged on the slots just written, so it has to come after them. The transition
                    // lands on the next tick, which nobody can see.
                    if (Settled(state, members, targetPos, settings))
                    {
                        if (state.Phase == SquadPhase.Breaking)
                            SetPhase(state, SquadPhase.Engaged, Time.realtimeSinceStartup);
                        else if (state.Phase == SquadPhase.Withdrawing)
                            StandDownToPost(formation.Key, state, members, settings, Time.realtimeSinceStartup);
                    }
                }
                else
                {
                    // No station and nothing to fight: issue no slots at all. Everything below is the station
                    // cycle - hold where the bout ended, then walk back and realign - and a group without a post
                    // has no home to be sent to. Its "post" is only wherever its bots happened to spawn, so
                    // running this would march idle duellists back across the map for no reason.
                    if (!settings.Post) continue;

                    // Waiting, either where the last bout ended or back on the post once returnDelay has run out.
                    // Slots are issued either way: leaving a bot without one hands movement back to its own
                    // spacing rules, and with a player stood nearby that means shadowing them around at
                    // passiveRange instead of holding station, which is the opposite of lingering.
                    //
                    // The bearing follows the position. Resting in the field, it keeps the way it came to rest;
                    // only actually going home realigns it to how the station was set up.
                    bool goHome = Time.realtimeSinceStartup - state.PhaseSince >= settings.ReturnDelay;
                    if (goHome)
                    {
                        state.Anchor = state.Post;

                        // Going home is where a gathered formation ends. Each batch reverts to its own, so they
                        // walk to their own posts instead of trailing the leading batch back to its one, and the
                        // next time they meet on a player they form up fresh. A no-op for a batch standing alone.
                        Disband(formation.Key, sendHome: true);
                    }

                    Vector2 home = goHome ? state.Post : state.Anchor;
                    Vector2 forward = MovementSolver.DirectionFromHeading(goHome ? state.PostHeading : state.RestHeading);

                    // The lane check needs somewhere to look, so it is given a point straight ahead of the line.
                    AssignSlots(members, home + forward, home, forward, state, settings);
                }
            }
        }

        public static void Reset()
        {
            _slots.Clear();
            _byGroup.Clear();
            _groups.Clear();
            _pendingWakes.Clear();
            _formations.Clear();
            _memberOf.Clear();
            _disbanding.Clear();
        }

        // Decides who stands with whom this tick. A batch that is Engaged merges into the formation of the
        // lowest-numbered batch on the same enemy; everyone else stands on their own. Only Engaged batches merge,
        // because posting, backing off and withdrawing are all measured against a batch's own post - two groups
        // re-forming on different posts have nothing to share.
        private static void BuildFormations()
        {
            foreach (var list in _formations.Values) list.Clear();

            // Drop batches that no longer exist, so a wiped one does not hold its formation open.
            _disbanding.Clear();
            foreach (var pair in _memberOf)
                if (!_byGroup.TryGetValue(pair.Key, out List<BotController> live) || live.Count == 0)
                    _disbanding.Add(pair.Key);
            for (int i = 0; i < _disbanding.Count; i++)
                _memberOf.Remove(_disbanding[i]);

            // New joins: a batch fighting someone another batch is already fighting throws in with them. Lowest
            // batch number leads, so the choice is stable no matter what order they arrive in.
            foreach (var group in _byGroup)
            {
                if (group.Value.Count == 0 || _memberOf.ContainsKey(group.Key)) continue;
                if (!(_groups[group.Key].TargetId is int targetId)) continue;

                foreach (var other in _byGroup)
                {
                    if (other.Key == group.Key || other.Value.Count == 0) continue;

                    // Compare against the FORMATION's target, not the batch's. Once a batch joins, only the lead
                    // is stepped from then on, so a follower's own TargetId freezes at whoever it was fighting
                    // when it joined and is never cleared. Reading that directly let a bot provoked long
                    // afterwards match a stale name, join the old formation, and drag a group that had already
                    // finished its bout back into a fight nobody had picked with them.
                    int otherLead = LeadOf(other.Key);
                    if (!(_groups[otherLead].TargetId is int otherTarget) || otherTarget != targetId) continue;

                    // Join whatever they are already in rather than renumbering it. Picking the lower of the two
                    // would re-point this pair and leave the rest of an existing formation still following the
                    // old lead, quietly splitting it in half. Lowest only decides it when both are loose, which
                    // keeps that case stable whatever order they are seen in.
                    if (otherLead != other.Key)
                    {
                        _memberOf[group.Key] = otherLead;
                    }
                    else
                    {
                        int lead = Mathf.Min(group.Key, other.Key);
                        _memberOf[group.Key] = lead;
                        _memberOf[other.Key] = lead;
                    }

                    // Its own fight state is meaningless from here: it follows the lead's. Cleared so nothing can
                    // read it back as a live target later.
                    _groups[group.Key].TargetId = null;
                    break;
                }
            }

            foreach (var group in _byGroup)
            {
                if (group.Value.Count == 0) continue;

                int key = LeadOf(group.Key);
                if (!_formations.TryGetValue(key, out List<BotController> members))
                {
                    members = new List<BotController>();
                    _formations[key] = members;
                }

                members.AddRange(group.Value);
            }
        }

        private static int LeadOf(int groupId) => _memberOf.TryGetValue(groupId, out int lead) ? lead : groupId;

        // The formation is finished: every batch in it goes back to being its own again, which is what sends each
        // one to its own post rather than to whichever post happened to be leading.
        private static void Disband(int formationKey, bool sendHome)
        {
            float now = Time.realtimeSinceStartup;

            _disbanding.Clear();
            foreach (var pair in _memberOf)
                if (pair.Value == formationKey) _disbanding.Add(pair.Key);

            for (int i = 0; i < _disbanding.Count; i++)
            {
                int batch = _disbanding[i];
                _memberOf.Remove(batch);
                if (!_groups.TryGetValue(batch, out GroupState member)) continue;

                // A batch's own fight state stopped being stepped the moment it joined, so it is still holding
                // whatever it was doing back then. Left alone it would revert mid-fight and charge a target it
                // last saw minutes ago.
                member.TargetId = null;
                member.Phase = SquadPhase.Posted;
                member.Strength = 0;

                if (sendHome)
                {
                    // The formation already sat out its returnDelay, so its members go now rather than lingering
                    // a second time over a bout they have already finished.
                    member.PhaseSince = 0f;
                    member.Anchor = member.Post;
                }
                else
                {
                    // Broken up because the bout ended, not because it was time to leave. Each batch stands where
                    // it is and starts its own wait, so the next fight can begin from here.
                    member.PhaseSince = now;
                    if (_byGroup.TryGetValue(batch, out List<BotController> own) && own.Count > 0)
                    {
                        member.Anchor = MeanPosition(own);
                        member.RestHeading = MeanHeading(own);
                    }
                }
            }

            if (_groups.TryGetValue(formationKey, out GroupState lead)) lead.Strength = 0;
        }

        // The group has lost its last member. Its phase is frozen wherever the fight left it, and nothing will
        // move it on because the tick loop only runs over live bots - so the first replacement to arrive would
        // wake up mid-bout, be judged shorthanded, and have to withdraw and re-form before its groupmates were
        // allowed to spawn at all. Clearing it here means a wiped group comes back all at once.
        //
        // The post, strength and resting bearing are kept: the station outlives the bots standing on it.
        public static void OnGroupEmptied(int groupId)
        {
            _memberOf.Remove(groupId);

            // Other batches are still fighting under this one's state - it was leading a formation that outlives
            // it - so the bout is not over and its phase must not be cleared out from under them.
            foreach (var pair in _memberOf)
                if (pair.Value == groupId) return;

            if (!_groups.TryGetValue(groupId, out GroupState state)) return;

            state.TargetId = null;
            state.Phase = SquadPhase.Posted;
            state.PhaseSince = Time.realtimeSinceStartup;
            state.Strength = 0;
        }

        // Whether the group's bout is over, which is when a held replacement may appear. An unknown group has
        // never formed, so there is nothing to wait for.
        //
        // Withdrawing counts as over. The bout is decided the moment the group breaks off; the withdrawal is only
        // it walking away from one. Waiting for Posted instead meant the survivors of a 3v1 had to finish
        // disengaging before anyone could respawn, so replacements trickled back one at a time. Coming back
        // during the withdrawal lets the group re-form where it stands, which is the point of not going home.
        //
        // Phase only, deliberately: this state goes stale the moment nothing is ticking, and the tick loop stops
        // when the last bot dies - which is precisely when a held replacement is waiting to hear. BotManager
        // checks for live members against its own tracking before calling this (see GroupBetweenBouts).
        public static bool IsBoutOver(int groupId) =>
            !_groups.TryGetValue(LeadOf(groupId), out GroupState state)
            || state.Phase == SquadPhase.Posted
            || state.Phase == SquadPhase.Withdrawing;

        // A member was killed. Waking only on a blocked hit misses the case that matters most to a drill: a clean
        // opening stab that kills before the guard comes up leaves the rest of the group standing there. Called
        // from the kill callback via BotManager, which is what knows the victim's group.
        public static void OnMemberKilled(int groupId, int victimPlayerId, int killerPlayerId)
        {
            if (groupId == 0 || killerPlayerId == victimPlayerId) return;

            // Never turn a group onto one of its own. Formations exist to cut down friendly fire; answering it
            // with a revenge order would be the opposite.
            if (_byGroup.TryGetValue(groupId, out List<BotController> members))
                for (int i = 0; i < members.Count; i++)
                    if (members[i].PlayerId == killerPlayerId) return;

            // Filed against the formation, not the batch: it is the formation that wakes, and the victim's own
            // batch may not even be the one leading it.
            _pendingWakes[LeadOf(groupId)] = killerPlayerId;
        }

        // Drives the station cycle: wait, break off, fight, return. A group without Post is always fighting, which
        // is the behaviour every squad had before stations existed.
        private static void StepPhase(int groupId, GroupState state, List<BotController> members, SquadSettings settings)
        {
            float now = Time.realtimeSinceStartup;

            // Always taken, even when it goes unused, so a kill during a fight cannot sit around and re-trigger
            // once the group is back at its post.
            int? killer = null;
            if (_pendingWakes.TryGetValue(groupId, out int k))
            {
                _pendingWakes.Remove(groupId);
                killer = k;
            }

            if (!settings.Post)
            {
                // No station: the group forms around whoever has actually attacked one of its members, and only
                // that. It must NOT fall back to who they are looking at - a bot waiting to be provoked reports
                // the nearest player simply because it is watching them, so reading that would have passive bots
                // form up on a passer-by and then be walked to combat range by their own slots, engaging someone
                // who never touched them. Being attacked is the whole trigger.
                bool wasIdle = state.TargetId == null;
                state.Phase = SquadPhase.Engaged;
                state.TargetId = FirstProvoker(members);

                // Starting a fresh fight: form up where the members actually are. Without a post the anchor is
                // still sitting wherever they spawned, and a bot that has since walked anywhere would be dragged
                // back toward it by its own slot.
                if (wasIdle && state.TargetId != null)
                {
                    state.Anchor = MeanPosition(members);
                    state.SharedStabHigh = Random.value < 0.5f;
                }
                return;
            }

            // Down a member and told to regroup: the bout is over. This is how a human pair treats a 2v1 when one
            // of them dies - the drill was never a 1v1 - rather than fighting on short-handed. Strength is the
            // formation's full size, so it re-arms only once a replacement has landed. Needs the Replace death
            // policy; with Kick or None nobody comes back and the station stays shut.
            // Too few left to be worth fighting. A threshold rather than a flag so a 3v1 can go on as a 2v1 and
            // only stop when it would become a 1v1: set it to the smallest count the drill is still about.
            //
            // Capped by how big this group is meant to be, which is what lets one value be a sane default at any
            // size. A bot fighting on its own is a formation of one and its drill is a 1v1, so a threshold of 2
            // must not apply to it - uncapped it would read as permanently shorthanded and never accept a fight
            // at all.
            //
            // The larger of the two measures, and it has to be both. Strength alone breaks a summoned pair: the
            // formation is torn down at the end of every bout, Strength re-measures against the lone survivor,
            // and the group cheerfully re-arms at half size instead of waiting for its replacement - which also
            // slams shut the window a held replacement is waiting on. FoundedCount alone breaks a gathered group,
            // whose batches were each founded as one.
            int minMembers = Mathf.Min(settings.MinMembers, Mathf.Max(state.Strength, state.FoundedCount));
            bool shortHanded = members.Count < minMembers;
            bool targetLive = TargetStillValid(state, settings);

            // Time to break off: either the bout is over or the target is gone.
            if ((state.Phase == SquadPhase.Breaking || state.Phase == SquadPhase.Engaged) && (shortHanded || !targetLive))
            {
                // Withdraw rather than switch off, while whoever we were fighting is still standing. A bot that
                // drops its guard and walks home is free kills for anyone who follows it, and a human giving
                // ground in a lost 2v1 keeps countering the whole way back.
                if (targetLive)
                {
                    SetPhase(state, SquadPhase.Withdrawing, now);
                }
                else
                {
                    StandDownToPost(groupId, state, members, settings, now);
                    return;
                }
            }

            // Withdrawing ends when the enemy is gone or it has taken too long - a player who follows a retreating
            // group forever must not be able to keep the station shut. Having re-formed is the other way out, and
            // is checked once this tick's slots exist to measure against.
            if (state.Phase == SquadPhase.Withdrawing && (!targetLive || now - state.PhaseSince > WithdrawTimeout))
            {
                StandDownToPost(groupId, state, members, settings, now);
                return;
            }

            switch (state.Phase)
            {
                case SquadPhase.Posted:
                    // Provoking any one member wakes the whole group onto whoever did it, where provoking means
                    // either hitting a guard or killing a member outright. Whether they reset the distance first
                    // or pile straight in from where the blow landed is the Breakoff setting.
                    if (!shortHanded && (FirstProvoker(members) ?? killer) is int provoker)
                    {
                        state.TargetId = provoker;

                        // The fight starts where the group is standing, NOT at the post. They are often not on it
                        // - lingering out a returnDelay, or still walking home from the last bout - and anchoring
                        // to the post there sends everyone marching back to it before they will engage, which is
                        // both silly to watch and exactly what returnDelay exists to avoid.
                        state.Anchor = MeanPosition(members);
                        state.SharedStabHigh = Random.value < 0.5f;
                        SetPhase(state, settings.Breakoff ? SquadPhase.Breaking : SquadPhase.Engaged, now);
                    }
                    break;

                case SquadPhase.Breaking:
                    // Give up backing off if a player body blocking a member has stalled it this long. The
                    // formation-is-set case is checked in Refresh, once this tick's slots exist to measure against.
                    if (now - state.PhaseSince > BreakoffTimeout)
                        SetPhase(state, SquadPhase.Engaged, now);
                    break;
            }

            // Re-asserted every tick rather than once on waking, because a member killed mid-drill is replaced by
            // a bot that starts passive by design (being provoked is deliberately not inherited). Without this the
            // replacement would stand in the formation and never fight.
            if (state.Phase != SquadPhase.Posted && state.TargetId is int targetId)
                WakeAll(members, targetId);
        }

        // Fully off: guard down, target forgotten, waiting to be provoked again. The anchor is left where the
        // group is standing rather than snapped to the post, because returnDelay may hold them here for a while
        // first; the waiting branch moves it to the post once that lapses.
        private static void StandDownToPost(int formationKey, GroupState state, List<BotController> members, SquadSettings settings, float now)
        {
            for (int i = 0; i < members.Count; i++)
                AsMember(members[i])?.StandDown();

            state.TargetId = null;
            state.Anchor = MeanPosition(members);

            // Keep whatever way they finished facing. A formation that snapped back to its original bearing every
            // time a bout ended would spin on the spot for no reason: the post's heading is how the station was
            // set up, not a direction the group has to hold everywhere it goes.
            state.RestHeading = MeanHeading(members);

            SetPhase(state, SquadPhase.Posted, now);

            // A formation that was never a unit does not outlive the fight that created it. Duellists who stood
            // apart while the same player swung at all of them go back to being separate bots the moment it is
            // over, so the next fight has to provoke each of them again rather than one of them pulling in
            // everyone it stood beside last time.
            //
            // Judged on minMembers, because that is the setting that says the group has a size worth preserving -
            // a drill about being outnumbered. Coordination cannot answer it: on that axis both ends are
            // deliberate, so a pair agreeing never to updown is as coordinated as one agreeing always to.
            if (settings.MinMembers <= 0) Disband(formationKey, sendHome: false);
        }

        private static void SetPhase(GroupState state, SquadPhase phase, float now)
        {
            if (state.Phase == phase) return;

            state.Phase = phase;
            state.PhaseSince = now;
        }

        private static void WakeAll(List<BotController> members, int targetId)
        {
            for (int i = 0; i < members.Count; i++)
                AsMember(members[i])?.Engage(targetId);
        }

        // The group has re-formed: it has its distance and everyone is on their slot. A group that is leaving has
        // no distance to reach - it re-forms on the spot - so for that one, being back in formation is the whole
        // test. Checking a range there would never pass and would strand it in the withdrawal until the timeout.
        private static bool Settled(GroupState state, List<BotController> members, Vector2 targetPos, SquadSettings settings)
        {
            if (state.Phase != SquadPhase.Withdrawing)
            {
                float distance = Vector2.Distance(targetPos, state.Anchor);
                if (Mathf.Abs(distance - settings.BreakoffRange) > AnchorTolerance) return false;
            }

            for (int i = 0; i < members.Count; i++)
            {
                if (!_slots.TryGetValue(members[i].PlayerId, out SquadSlot slot)) return false;
                if (Vector2.Distance(Planar(members[i]), slot.Position) > SettleRadius) return false;
            }

            return true;
        }

        // A target is worth keeping while it is alive, spawned, and still near enough to the post to count as
        // using the station rather than having left it. ResetRange 0 lifts the distance limit entirely, matching
        // targetRange and separationRange: the bout then ends only on a death or on going shorthanded.
        private static bool TargetStillValid(GroupState state, SquadSettings settings)
        {
            IPlayer target = state.TargetId is int id ? StateTracker.GetPlayerById(id) : null;
            if (!IsLiveTarget(target)) return false;
            if (settings.ResetRange <= 0f) return true;

            Vector3 t = target.PlayerObject.transform.position;
            return Vector2.Distance(new Vector2(t.x, t.z), state.Post) <= settings.ResetRange;
        }

        // Spawned AND alive. The aliveness check is the important half: a corpse keeps its PlayerObject for a
        // moment after death, so testing the object alone leaves the group swinging at a body, and at whoever
        // respawns on that id, for the tick or two before it is cleaned up.
        private static bool IsLiveTarget(IPlayer target) => target?.PlayerObject != null && target.IsAlive;

        // Average bearing of the members right now. Summed as direction vectors rather than as degrees: averaging
        // the numbers would take 350 and 10 to 180 and point the formation backwards.
        private static float MeanHeading(List<BotController> members)
        {
            Vector2 sum = Vector2.zero;
            for (int i = 0; i < members.Count; i++)
                sum += MovementSolver.DirectionFromHeading(members[i].Heading ?? 0f);

            return sum.sqrMagnitude > 1e-4f ? MovementSolver.HeadingOf(sum) : 0f;
        }

        // Midpoint of the members as they stand right now.
        private static Vector2 MeanPosition(List<BotController> members)
        {
            if (members.Count == 0) return Vector2.zero;

            Vector2 mean = Vector2.zero;
            for (int i = 0; i < members.Count; i++)
                mean += Planar(members[i]);

            return mean / members.Count;
        }

        private static int? FirstProvoker(List<BotController> members)
        {
            for (int i = 0; i < members.Count; i++)
                if (AsMember(members[i])?.ProvokedBy is int id) return id;

            return null;
        }

        // The group's state, with the post measured from its members' midpoint. A group's bots arrive over several
        // ticks rather than all at once, so the post is re-measured while the group is still growing and settles
        // as soon as it stops. It never moves after that: losing a member does not shrink it back, or a station
        // would walk across the map over a session as its bots died and were replaced in new places.
        private static GroupState StateFor(int groupId, List<BotController> members)
        {
            if (!_groups.TryGetValue(groupId, out GroupState state))
            {
                state = new GroupState { PhaseSince = Time.realtimeSinceStartup };
                _groups[groupId] = state;
            }

            if (members.Count <= state.FoundedCount) return state;

            state.Post = MeanPosition(members);
            state.PostHeading = MeanHeading(members);
            state.RestHeading = state.PostHeading;   // never fought yet, so it rests the way it was set up
            state.Anchor = state.Post;
            state.FoundedCount = members.Count;

            return state;
        }

        // Holds a range band from the enemy rather than following them directly. That distinction is what keeps
        // the formation calm: an enemy circling at a steady distance stays inside the band and never moves the
        // anchor, so the members simply rotate around it, while an enemy closing in or backing off drags it along.
        // Repositioning is capped at FollowSpeed so the anchor cannot outrun the members chasing their slots.
        private static Vector2 MoveAnchor(GroupState state, Vector2 targetPos, float standoff, float deltaTime)
        {
            Vector2 anchor = state.Anchor;
            Vector2 toTarget = targetPos - anchor;
            float distance = toTarget.magnitude;

            if (distance > 1e-4f)
            {
                Vector2 dir = toTarget / distance;
                float step = FollowSpeed * deltaTime;

                if (distance > standoff + AnchorTolerance)
                    anchor += dir * Mathf.Min(step, distance - standoff);
                else if (distance < standoff - AnchorTolerance)
                    anchor -= dir * Mathf.Min(step, standoff - distance);
            }

            state.Anchor = anchor;
            return anchor;
        }

        // Groups the squad-enabled bots by the batch they spawned in.
        private static void Bucket(IReadOnlyList<BotController> bots)
        {
            foreach (var list in _byGroup.Values) list.Clear();

            for (int i = 0; i < bots.Count; i++)
            {
                BotController bot = bots[i];
                if (bot.GroupId == 0) continue;                                     // no batch, so nothing to hold it to
                if (!(bot.Ai is ISquadMember member)) continue;

                // Either half is reason enough to be here: a formation needs slots to fight from, and a station
                // needs one to stand on even when it is alone and never forms up with anybody.
                if (!member.WantsSquad && !member.SquadSettings.Post) continue;

                // Initialized, not merely spawned: a bot is teleported to where it was summoned as it spawns, and
                // counting it before that would found the group's post at the game's spawn point instead.
                if (!bot.Initialized || bot.Position == null) continue;

                if (!_byGroup.TryGetValue(bot.GroupId, out List<BotController> group))
                {
                    group = new List<BotController>();
                    _byGroup[bot.GroupId] = group;
                }

                group.Add(bot);
            }

            // Drop buckets nobody is using any more so the dictionary doesn't grow across a long session. The
            // group state itself is kept: a station whose members are all mid-replacement must not lose its post.
            _finishedGroups.Clear();
            foreach (var pair in _byGroup)
                if (pair.Value.Count == 0) _finishedGroups.Add(pair.Key);
            for (int i = 0; i < _finishedGroups.Count; i++)
                _byGroup.Remove(_finishedGroups[i]);
        }

        // Lays out one group on its anchor and records each member's slot.
        private static void AssignSlots(List<BotController> group, Vector2 facePos, Vector2 anchor, Vector2 forward, GroupState state, SquadSettings settings)
        {
            // The line the members stand on is held square to the enemy, so the gap between them always faces the
            // enemy. As the enemy moves around the anchor this line turns with them and the members ride the
            // circle, one giving ground while the other comes forward.
            Vector2 right = new Vector2(-forward.y, forward.x);

            // Ordering along that line keeps every bot on the side it is already on, so the slots turn with the
            // enemy instead of being swapped between bots and sending them through each other.
            group.Sort((a, b) => Vector2.Dot(Planar(a) - anchor, right).CompareTo(Vector2.Dot(Planar(b) - anchor, right)));

            // The squad shares its most recent successful guard. A block means that stab is spent and its thrower
            // is recovering, which is as true for the member standing next to the one who blocked as it is for the
            // blocker: both are free to counter. Sharing it is what lets a pair answer together.
            float blockTime = 0f;
            for (int i = 0; i < group.Count; i++)
                blockTime = Mathf.Max(blockTime, CombatTracker.LastBlockTime(group[i].PlayerId));

            float span = (group.Count - 1) * settings.Spacing;

            for (int i = 0; i < group.Count; i++)
            {
                float offset = (i * settings.Spacing) - (span * 0.5f);

                _slots[group[i].PlayerId] = new SquadSlot
                {
                    Position = anchor + right * offset,
                    LaneClear = IsLaneClear(group, i, facePos, settings),
                    Members = group.Count,
                    BlockTime = blockTime,

                    // Alternate the swing along the line so neighbours never throw the same way and one guard
                    // cannot stop them both. Taking it from the position in the line rather than rolling it per
                    // bot also keeps a bot on the same direction tick to tick, since the sort above holds each
                    // one on its own side.
                    StabHigh = (i % 2) == 0,
                    SharedHigh = state.SharedStabHigh,

                    Phase = state.Phase,
                    Facing = MovementSolver.HeadingOf(forward),
                };
            }
        }

        // Whether this member can swing at the target without a squadmate in the way. Only squadmates are checked:
        // they are the ones lined up on the same enemy, and they are the friendly fire actually being caused here.
        private static bool IsLaneClear(List<BotController> group, int index, Vector2 targetPos, SquadSettings settings)
        {
            Vector2 from = Planar(group[index]);
            Vector2 toTarget = targetPos - from;
            float reach = toTarget.magnitude;
            if (reach < 1e-4f) return true;

            Vector2 dir = toTarget / reach;

            for (int i = 0; i < group.Count; i++)
            {
                if (i == index) continue;

                Vector2 toMate = Planar(group[i]) - from;
                float along = Vector2.Dot(toMate, dir);
                if (along <= 0f || along > reach) continue; // behind us, or further off than the target

                float lateral = Mathf.Abs(toMate.x * dir.y - toMate.y * dir.x); // distance from the swing line
                if (lateral < settings.LaneHalfWidth) return false;
            }

            return true;
        }

        private static ISquadMember AsMember(BotController bot) => bot.Ai as ISquadMember;

        private static SquadSettings SettingsOf(BotController bot) =>
            bot.Ai is ISquadMember member
                ? member.SquadSettings
                : new SquadSettings { Spacing = 0.9f, LaneHalfWidth = 0.5f, Standoff = 1.5f, BreakoffRange = 6f, ResetRange = 15f };

        private static Vector2 Planar(BotController bot)
        {
            Vector3 p = bot.Position ?? Vector3.zero;
            return new Vector2(p.x, p.z);
        }
    }
}
