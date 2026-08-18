using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using MDS.ConfigVariables;
using MDS.Core;

// Central bot subsystem: spawns and tracks bots, assigns AI and death policy, and runs a single tick
// coroutine that lives only while at least one bot is active. Bots are dropped each new round (mirroring
// StateTracker) and auto-clear on map change via assembly reload. Lifecycle hooks are called by StateTracker.
// The command layer resolves caller context; this layer takes explicit data only.

namespace MDS.Systems
{
    public static class BotManager
    {
        private const float TickInterval = 0.05f;                 // 20 Hz - tunable
        private const float GhostTimeoutSeconds = 5f;             // drop bots that joined but never spawned
        // Give up on a spawn request the game never delivered a bot for. Joining bots are paired with requests in
        // arrival order, so a request that never lands would otherwise sit at the front of the queue and hand the
        // next bot to join the wrong placement, spec and AI. Bots normally join within a second, and a ten-bot
        // formation under a full server took about five, so this is well clear of legitimate latency.
        private const float PendingSpawnTimeoutSeconds = 15f;

        // Defaults + timings read live from configurables (settable via rc set / map config variables).
        private static BotAiEnum DefaultAi =>
            ((BotDefaultAiConfigurable)ConfigurableRegistry.Get(ConfigurableEnum.BotDefaultAi)).DefaultAi;
        private static BotDeathPolicy DefaultDeathPolicy =>
            ((BotDefaultDeathConfigurable)ConfigurableRegistry.Get(ConfigurableEnum.BotDefaultDeathPolicy)).DefaultPolicy;
        private static float KickDelaySeconds =>
            ((BotKickDelayConfigurable)ConfigurableRegistry.Get(ConfigurableEnum.BotKickDelay)).KickDelay;
        private static float ReplaceDelaySeconds =>
            ((BotReplaceDelayConfigurable)ConfigurableRegistry.Get(ConfigurableEnum.BotReplaceDelay)).ReplaceDelay;

        private static readonly List<BotController> _bots = new();
        private static readonly Queue<PendingBotSpawn> _pending = new();
        private static Coroutine _tickRoutine;

        // Bumped whenever tracking is torn down (new round, remove all). Delayed work runs on the
        // DontDestroyOnLoad coroutine runner, so it outlives a map change; anything scheduled in an earlier
        // generation must not spawn into the current one. A stale replacement would enqueue a pending entry
        // holding the previous round's AI and placement, and since joining bots are paired with pending entries
        // in arrival order, that one stale entry shifts every assignment after it.
        private static int _generation;

        // How long a held replacement will wait for its group's bout to finish, and how often it checks. The cap
        // matters: a drill nobody ever finishes would otherwise swallow the bot permanently.
        private const float ReplacementHoldTimeout = 120f;
        private const float ReplacementHoldPoll = 0.5f;

        // Bots whose death was a casualty of their group's own bout, filled in by OnPlayerKilled. Only these
        // hold their replacement back; see DeathKickRoutine. It has to be a side channel because OnBotDied runs
        // first and does not know who did it - the game fires OnPlayerHurt before OnPlayerKilledPlayer, and for
        // a death with no killer at all (an admin slay, the environment) it never fires the second one.
        private static readonly HashSet<int> _boutCasualties = new();

        // Handed out one per spawn batch, so bots summoned together can be recognised as a formation later. Never
        // reused within a session, so a stale id can't quietly adopt a bot into a group it was never part of.
        private static int _nextGroupId = 1;

        public static IReadOnlyList<BotController> Bots => _bots;

        // Read by other systems that schedule delayed spawns (see LineManager) so they can drop stale work.
        public static int Generation => _generation;

        // Command surface.

        // spec null means a fully random spawn (carbonPlayers spawn). placement positions and faces each bot on spawn.
        // predecessor is supplied only by the Replace path, so the replacement can resume the dead bot's
        // standing order (see IBotAi.InheritFrom); every other caller leaves it null.
        // guardTargetId is set by the summon commands so a guardian AI escorts the player it was summoned onto.
        // groupId 0 means "this is a new batch", so every ordinary summon forms its own group; the Replace path
        // passes the dead bot's id instead, so a replacement rejoins the station it came from.
        public static void SpawnBots(int count, BotSpawnSpec spec, BotAiEnum ai, BotDeathPolicy death, BotPlacement? placement, IBotAi predecessor = null, int? guardTargetId = null, int groupId = 0)
        {
            float requestedAt = Time.realtimeSinceStartup;
            if (groupId == 0) groupId = _nextGroupId++;

            for (int i = 0; i < count; i++)
                _pending.Enqueue(new PendingBotSpawn { Spec = spec, Ai = ai, Death = death, Placement = placement, Predecessor = predecessor, RequestedAt = requestedAt, GuardTargetId = guardTargetId, GroupId = groupId });

            if (spec == null)
            {
                CarbonPlayerCommands.Spawn(count);
            }
            else
            {
                for (int i = 0; i < count; i++)
                    CarbonPlayerCommands.SpawnSpecific(spec);
            }

            Logger.Log($"Requested {count} bot(s). Spec: {(spec == null ? "random" : $"{FactionTokens.DisplayName(spec.Faction)}/{spec.Class}")}, AI {ai}, death {death}.", LogLevel.INFO);
        }

        // Spawns one bot per placement, all sharing the same spec/ai/death (used by line formations).
        // guardTargetId is set when the line was summoned onto a player, so a guardian AI escorts them.
        public static void SpawnBotsAt(IReadOnlyList<BotPlacement> placements, BotSpawnSpec spec, BotAiEnum ai, BotDeathPolicy death, int? guardTargetId = null)
        {
            if (spec == null)
            {
                Logger.Log("SpawnBotsAt requires a spec (formations can't use random spawn).", LogLevel.WARNING);
                return;
            }

            float requestedAt = Time.realtimeSinceStartup;
            int groupId = _nextGroupId++;   // a line is one formation

            foreach (var placement in placements)
            {
                _pending.Enqueue(new PendingBotSpawn { Spec = spec, Ai = ai, Death = death, Placement = placement, RequestedAt = requestedAt, GuardTargetId = guardTargetId, GroupId = groupId });
                CarbonPlayerCommands.SpawnSpecific(spec);
            }

            Logger.Log($"Requested {placements.Count} bot(s) in formation. Spec: {FactionTokens.DisplayName(spec.Faction)}/{spec.Class}, AI {ai}, death {death}.", LogLevel.INFO);
        }

        public static bool SetAi(int playerId, BotAiEnum ai)
        {
            var bot = _bots.FirstOrDefault(b => b.PlayerId == playerId);
            if (bot == null) return false;

            bot.SetAi(BotAiFactory.Create(ai));
            return true;
        }

        public static bool SetDeath(int playerId, BotDeathPolicy policy)
        {
            var bot = _bots.FirstOrDefault(b => b.PlayerId == playerId);
            if (bot == null) return false;

            bot.SetDeathPolicy(policy);
            return true;
        }

        public static bool RemoveBot(int playerId)
        {
            if (_bots.All(b => b.PlayerId != playerId)) return false;

            CarbonPlayerCommands.Despawn(playerId); // serverAdmin kick; disconnect callback also untracks (idempotent)
            Untrack(playerId);
            return true;
        }

        public static void RemoveAll()
        {
            foreach (var bot in _bots.ToList())
                CarbonPlayerCommands.Despawn(bot.PlayerId);

            _generation++; // drop any scheduled replacement, so a removed bot can't come back
            _bots.Clear();
            _pending.Clear();
            SquadCoordinator.Reset();   // the formations went with the bots; Reset() does the same on a new round
            StopTicking();
        }

        // Lifecycle hooks (called by StateTracker).

        public static void OnBotJoined(IPlayer bot)
        {
            if (_bots.Any(b => b.PlayerId == bot.PlayerId)) return;

            // Clear out expired requests first: this bot must never be paired with one from minutes ago.
            PrunePendingSpawns();

            // Joining bots are paired with spawn requests in arrival order, since the join callback carries no
            // link back to the request. A bot with no pending request is therefore unexpected (something spawned
            // it outside the mod, or the queue desynced) and it falls back to the configured defaults.
            if (_pending.Count == 0)
                Logger.Log($"Bot {bot.PlayerId} joined with no pending spawn request; using defaults (AI {DefaultAi}).", LogLevel.WARNING);

            PendingBotSpawn p = _pending.Count > 0
                ? _pending.Dequeue()
                : new PendingBotSpawn { Spec = null, Ai = DefaultAi, Death = DefaultDeathPolicy, Placement = null };

            IBotAi ai = BotAiFactory.Create(p.Ai);

            // A Replace replacement resumes the standing order of the bot it replaces; without this it would
            // come back identical in every respect except that it had forgotten what it was doing.
            if (p.Predecessor != null)
                ai.InheritFrom(p.Predecessor);

            // A bot summoned onto a player escorts them, if its AI knows how to.
            if (p.GuardTargetId is int wardId && ai is IGuardianAi guardian)
                guardian.SetGuardTarget(wardId);

            _bots.Add(new BotController(bot, ai, p.Spec, p.Death, p.Placement, p.GroupId));
            Logger.Log($"Bot {bot.PlayerId} tracked (AI {p.Ai}, death {p.Death}). Active bots: {_bots.Count}.", LogLevel.INFO);

            EnsureTicking();
        }

        public static void OnBotSpawned(IPlayer bot)
        {
            var controller = _bots.FirstOrDefault(b => b.PlayerId == bot.PlayerId);
            controller?.OnSpawned();
        }

        public static void OnBotDied(IPlayer bot)
        {
            var controller = _bots.FirstOrDefault(b => b.PlayerId == bot.PlayerId);
            if (controller == null) return;

            // A kick is already scheduled (e.g. it was killed again during the delay) - ignore.
            if (controller.IsAwaitingKick) return;

            // Capture state at the moment of death - position/GameObject may be gone moments later.
            BotDeathPolicy policy = controller.DeathPolicy;
            Vector3? deathPos = controller.Position;
            float? deathHeading = controller.Heading;
            BotSpawnSpec spec = BuildReplacementSpec(controller);
            BotAiEnum ai = controller.AiType;
            IBotAi predecessorAi = controller.Ai;   // Replace: lets the replacement resume its standing order
            int groupId = controller.GroupId;       // Replace: keeps the replacement in its formation

            // Whether the replacement is *allowed* to wait out the bout instead of walking straight back into
            // it. Without the hold, a 3v1 is briefly a 2v1 and then a 3v1 again, so the shorthanded fight the
            // drill is about never actually happens. Whether it actually waits is decided later, once the killer
            // is known: see _boutCasualties.
            bool holdReplacement = groupId != 0 && controller.Ai is MeleeAi melee && melee.HoldReplacement;

            switch (policy)
            {
                case BotDeathPolicy.None:
                    // Do nothing - the game auto-respawns it and the bot stays tracked.
                    Logger.Log($"Bot {bot.PlayerId} died (policy: None). No action taken.", LogLevel.DEBUG);
                    break;

                case BotDeathPolicy.Kick:
                    // Keep it tracked until the kick fires, so an auto-respawn during the delay re-joins
                    // as an already-tracked bot (guarded in OnBotJoined) rather than resetting to defaults.
                    Logger.Log($"Bot {bot.PlayerId} died (policy: Kick). Kicking in {KickDelaySeconds}s.", LogLevel.INFO);
                    controller.MarkAwaitingKick();
                    ScheduleDeathKick(bot.PlayerId, null, ai, policy, null);
                    break;

                case BotDeathPolicy.Replace:
                    controller.MarkAwaitingKick();

                    if (spec == null || !deathPos.HasValue)
                    {
                        string reason = spec == null ? "has no spawn spec (random bot)" : "position unavailable";
                        Logger.Log($"Bot {bot.PlayerId} died (policy: Replace) but {reason}. Kicking only (in {KickDelaySeconds}s).", LogLevel.WARNING);
                        ScheduleDeathKick(bot.PlayerId, null, ai, policy, null);
                        break;
                    }

                    Logger.Log($"Bot {bot.PlayerId} died (policy: Replace). Kicking in {KickDelaySeconds}s and respawning at {deathPos.Value}.", LogLevel.INFO);
                    ScheduleDeathKick(bot.PlayerId, spec, ai, policy, new BotPlacement(deathPos.Value, deathHeading), predecessorAi, groupId, holdReplacement);
                    break;
            }
        }

        // A player killed someone. Only interesting when the victim was one of our bots in a formation: a stab
        // clean enough to kill before the guard rises never registers as a block, so without this the rest of a
        // drill station would go on waiting while its partner was cut down in front of it.
        public static void OnPlayerKilled(int killerPlayerId, int victimPlayerId)
        {
            var victim = _bots.FirstOrDefault(b => b.PlayerId == victimPlayerId);
            if (victim == null) return;

            // A bot killing a bot *of its own faction* is the friendly-fire case being chased. Logged before the
            // wake below, since OnMemberKilled deliberately swallows a kill by one of the group's own.
            //
            // The faction test matters as soon as bots exist on both sides, which groupfight and xvx both do:
            // without it every ordinary cross-faction bot kill lands in the log as friendly fire and the record
            // is worthless for tuning. Unknown factions are treated as a match, keeping this on the same
            // fail-safe side as the aim clamp.
            var killer = _bots.FirstOrDefault(b => b.PlayerId == killerPlayerId);
            bool sameSide = killer != null
                && (killer.Bot.Faction == null || victim.Bot.Faction == null
                    || killer.Bot.Faction == victim.Bot.Faction);

            if (sameSide && killer.Position is Vector3 kp && victim.Position is Vector3 vp && killer.Heading is float kh)
                MeleeProbe.LogFriendlyFire(killerPlayerId, victimPlayerId,
                    new Vector2(kp.x, kp.z), kh, new Vector2(vp.x, vp.z));

            SquadCoordinator.OnMemberKilled(victim.GroupId, victimPlayerId, killerPlayerId);

            // Only a death at the hands of the bout's own opponent holds a replacement back. A member cut down by
            // another group's bot - which is what happens when a player lures a second group across the first -
            // is not a casualty of this bout, and holding for it leaves the group short until the timeout. Asked
            // after OnMemberKilled so the wake this kill files is already on the books.
            if (SquadCoordinator.IsBoutOpponent(victim.GroupId, killerPlayerId))
                _boutCasualties.Add(victimPlayerId);
        }

        public static void OnBotDisconnected(int playerId)
        {
            Untrack(playerId);
        }

        // Called on new round (StateTracker.NewRoundCleanup); bots are dropped from tracking.
        public static void Reset()
        {
            _generation++;
            _bots.Clear();
            _pending.Clear();
            _boutCasualties.Clear();
            CharacterTracker.Reset();
            MeleeProbe.Reset();
            CombatTracker.Reset();
            SquadCoordinator.Reset();
            StopTicking();
            Logger.Log("BotManager reset.", LogLevel.DEBUG);
        }

        // Internals.

        // Drops spawn requests the game never delivered a bot for. A request can go unanswered when the spawn is
        // refused (the carbon-bot limit) or the join is aborted, and since bots are paired with requests in
        // arrival order, a leftover would be handed to the next bot to join, giving it the wrong placement, spec
        // and AI for the rest of the round. Requests are enqueued in time order, so expired ones are always at
        // the front.
        // A copy of the tracked bots in random order. The copy is needed anyway, since a bot can be untracked
        // mid-tick, so shuffling it costs one pass over a handful of entries.
        private static List<BotController> ShuffledBots()
        {
            List<BotController> order = _bots.ToList();

            for (int i = order.Count - 1; i > 0; i--)
            {
                int j = Random.Range(0, i + 1);
                BotController swap = order[i];
                order[i] = order[j];
                order[j] = swap;
            }

            return order;
        }

        private static void PrunePendingSpawns()
        {
            float now = Time.realtimeSinceStartup;

            while (_pending.Count > 0 && now - _pending.Peek().RequestedAt > PendingSpawnTimeoutSeconds)
            {
                PendingBotSpawn stale = _pending.Dequeue();
                Logger.Log($"Spawn request (spec {(stale.Spec == null ? "random" : $"{FactionTokens.DisplayName(stale.Spec.Faction)}/{stale.Spec.Class}")}, AI {stale.Ai}) never produced a bot within {PendingSpawnTimeoutSeconds}s - discarded.", LogLevel.WARNING);
            }
        }

        private static void Untrack(int playerId)
        {
            int groupId = _bots.FirstOrDefault(b => b.PlayerId == playerId)?.GroupId ?? 0;

            int removed = _bots.RemoveAll(b => b.PlayerId == playerId);
            if (removed == 0) return;

            // That may have been the group's last member. Say so now rather than leaving it to the tick loop,
            // which is about to stop if this was the last bot on the server - and a group left frozen mid-bout
            // makes its own replacements queue up behind each other. See SquadCoordinator.OnGroupEmptied.
            if (groupId != 0 && _bots.All(b => b.GroupId != groupId))
                SquadCoordinator.OnGroupEmptied(groupId);

            Logger.Log($"Bot {playerId} untracked. Active bots: {_bots.Count}.", LogLevel.INFO);
            if (_bots.Count == 0) StopTicking();
        }

        // A death-triggered kick is delayed so the game can credit the killer and play the death before
        // the bot is removed (an immediate kick makes the bot vanish without crediting the kill).
        // A non-null replacementSpec (+ position) spawns a replacement once the kick fires (Replace).
        private static void ScheduleDeathKick(int playerId, BotSpawnSpec replacementSpec, BotAiEnum ai, BotDeathPolicy death, BotPlacement? placement, IBotAi predecessor = null, int groupId = 0, bool holdReplacement = false)
        {
            if (!UnityEngine.Application.isPlaying)
            {
                // Edit-mode (tests): no coroutine host / no real game - do it synchronously, no delays.
                KickBot(playerId);
                SpawnReplacement(replacementSpec, ai, death, placement, predecessor, groupId);
                return;
            }

            MonoBehaviourRunner.Instance.StartCoroutine(DeathKickRoutine(playerId, replacementSpec, ai, death, placement, predecessor, groupId, holdReplacement));
        }

        private static IEnumerator DeathKickRoutine(int playerId, BotSpawnSpec replacementSpec, BotAiEnum ai, BotDeathPolicy death, BotPlacement? placement, IBotAi predecessor, int groupId, bool holdReplacement)
        {
            // This runs on the DontDestroyOnLoad runner, so it keeps going across a map change. If the round
            // turned over while we waited, the bot and its slot are already gone and the replacement would spawn
            // into the new round carrying the old round's AI and placement, so drop the rest of the routine.
            int generation = _generation;

            // 1) Wait so the killer is credited and the death plays out before the bot is removed.
            yield return new WaitForSeconds(KickDelaySeconds);
            if (generation != _generation) yield break;
            KickBot(playerId);

            // 2) Spawn the replacement only after a short gap, so the kick fully frees the bot slot
            //    first (kicking and respawning back-to-back can make the spawnSpecific fail).
            yield return new WaitForSeconds(ReplaceDelaySeconds);
            if (generation != _generation) yield break;

            // 3) Optionally sit out the rest of the bout, so the group actually fights shorthanded instead of
            //    being topped back up mid-fight. Only for a bot the bout itself killed: anything else - a stray
            //    stab from a group next door, an admin slay - is not part of the drill, and waiting on a bout it
            //    was never in would strand the replacement until the timeout. Capped either way, because a bout
            //    that never ends - a drill left running while everyone wanders off - must not delete the bot for
            //    good.
            bool boutCasualty = _boutCasualties.Remove(playerId);
            if (holdReplacement && !boutCasualty)
                Logger.Log($"Bot {playerId} was not killed by its group's opponent; replacing without holding.", LogLevel.DEBUG);

            if (holdReplacement && boutCasualty)
            {
                float waitUntil = Time.realtimeSinceStartup + ReplacementHoldTimeout;
                while (!GroupBetweenBouts(groupId) && Time.realtimeSinceStartup < waitUntil)
                {
                    yield return new WaitForSeconds(ReplacementHoldPoll);
                    if (generation != _generation) yield break;
                }
            }

            SpawnReplacement(replacementSpec, ai, death, placement, predecessor, groupId);
        }

        private static void KickBot(int playerId)
        {
            // Untrack only now (not at death time) so the bot stays tracked through the delay.
            Untrack(playerId);
            CarbonPlayerCommands.Despawn(playerId);
            Logger.Log($"Bot {playerId} kicked; replacement (if any) in {ReplaceDelaySeconds}s.", LogLevel.DEBUG);
        }

        // Whether a held replacement may appear yet. The live-member check has to happen here rather than in the
        // coordinator: its own bookkeeping is rebuilt by the tick loop, and the tick loop stops once the last bot
        // is gone, so a group wiped out entirely would look forever mid-fight and strand its replacements until
        // the timeout. _bots is the authoritative list and is accurate whether or not anything is ticking.
        private static bool GroupBetweenBouts(int groupId) =>
            _bots.All(b => b.GroupId != groupId) || SquadCoordinator.IsBoutOver(groupId);

        private static void SpawnReplacement(BotSpawnSpec replacementSpec, BotAiEnum ai, BotDeathPolicy death, BotPlacement? placement, IBotAi predecessor, int groupId)
        {
            if (replacementSpec != null && placement.HasValue)
                SpawnBots(1, replacementSpec, ai, death, placement, predecessor, groupId: groupId);
        }

        // Builds the spec for a Replace replacement: keeps the intended faction/class, but fills in the bot's
        // actual name/regtag/uniformId. The game assigns those randomly when unspecified, so reusing the real
        // values makes the replacement match the bot it replaces. Returns null for random-spawned bots (which
        // have no spec to replay).
        private static BotSpawnSpec BuildReplacementSpec(BotController controller)
        {
            var spec = controller.Spec;
            if (spec == null) return null;

            var bot = controller.Bot;
            return new BotSpawnSpec(
                spec.Faction,
                spec.Class,
                bot.PlayerName,
                bot.RegimentTag,
                bot.UniformId);
        }

        private static void EnsureTicking()
        {
            // Coroutines require play mode. Skip in Edit Mode tests to avoid StartCoroutine throwing.
            if (!UnityEngine.Application.isPlaying) return;
            if (_tickRoutine == null && _bots.Count > 0)
                _tickRoutine = MonoBehaviourRunner.Instance.StartCoroutine(TickLoop());
        }

        private static void StopTicking()
        {
            if (_tickRoutine == null) return;

            MonoBehaviourRunner.Instance.StopCoroutine(_tickRoutine);
            _tickRoutine = null;
        }

        private static IEnumerator TickLoop()
        {
            // Like the other delayed work, this runs on the DontDestroyOnLoad runner. If a tick loop from an
            // earlier generation is somehow still alive (a stale coroutine handle, or statics reset out from
            // under it), it would drive the same bots alongside the current loop and the two sets of input
            // commands fight each other, leaving bots crawling back and forth. Exit as soon as that's detected.
            int generation = _generation;

            while (_bots.Count > 0)
            {
                if (generation != _generation) yield break;

                float now = Time.realtimeSinceStartup;

                // Report an unanswered spawn request promptly, even when no further bots join to trigger it.
                PrunePendingSpawns();

                // One shared snapshot of all spawned players/bots per tick, so neighbour-aware steering
                // (separation, collision avoidance) sees everyone without an O(n) gather per bot.
                CharacterTracker.Refresh(TickInterval);

                // Lay out the squads before the bots decide, so each one reads a slot built from this tick.
                SquadCoordinator.Refresh(_bots, TickInterval);

                // Shuffled, so that bots which decide on the same tick do not resolve in a fixed order. It matters
                // wherever two of them want the same thing at once, the stab separation gate most of all: a
                // riposte ignores the attack cooldown and the top tiers have no reaction beats, so both members
                // of a pair reach the gate on the same tick with nothing to separate them. In spawn order the
                // same bot won every time and the pair always led with the same member.
                foreach (var bot in ShuffledBots())
                {
                    // Self-heal: a replacement that joined but never spawned (game rejected it, e.g. a
                    // carbon-bot limit) would otherwise stay tracked forever as a ghost.
                    if (bot.IsUnspawnedGhost(now, GhostTimeoutSeconds))
                    {
                        Logger.Log($"Bot {bot.PlayerId} joined but never spawned within {GhostTimeoutSeconds}s - dropping as ghost.", LogLevel.WARNING);
                        Untrack(bot.PlayerId);
                        continue;
                    }

                    bot.Tick(TickInterval);
                }

                yield return new WaitForSeconds(TickInterval);
            }

            _tickRoutine = null;
        }

        private struct PendingBotSpawn
        {
            public BotSpawnSpec Spec;
            public BotAiEnum Ai;
            public BotDeathPolicy Death;
            public BotPlacement? Placement;
            public IBotAi Predecessor;   // Replace only: AI of the bot being replaced, for InheritFrom
            public float RequestedAt;    // realtime the spawn was asked for, so a request that never lands expires
            public int? GuardTargetId;   // the player a guardian AI should escort, from the summon that placed it
            public int GroupId;          // the spawn batch, so bots summoned together fight as one formation
        }
    }
}
