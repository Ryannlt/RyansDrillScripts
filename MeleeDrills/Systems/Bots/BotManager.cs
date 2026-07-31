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

        public static IReadOnlyList<BotController> Bots => _bots;

        // Read by other systems that schedule delayed spawns (see LineManager) so they can drop stale work.
        public static int Generation => _generation;

        // Command surface.

        // spec null means a fully random spawn (carbonPlayers spawn). placement positions and faces each bot on spawn.
        // predecessor is supplied only by the Replace path, so the replacement can resume the dead bot's
        // standing order (see IBotAi.InheritFrom); every other caller leaves it null.
        public static void SpawnBots(int count, BotSpawnSpec spec, BotAiEnum ai, BotDeathPolicy death, BotPlacement? placement, IBotAi predecessor = null)
        {
            for (int i = 0; i < count; i++)
                _pending.Enqueue(new PendingBotSpawn { Spec = spec, Ai = ai, Death = death, Placement = placement, Predecessor = predecessor });

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
        public static void SpawnBotsAt(IReadOnlyList<BotPlacement> placements, BotSpawnSpec spec, BotAiEnum ai, BotDeathPolicy death)
        {
            if (spec == null)
            {
                Logger.Log("SpawnBotsAt requires a spec (formations can't use random spawn).", LogLevel.WARNING);
                return;
            }

            foreach (var placement in placements)
            {
                _pending.Enqueue(new PendingBotSpawn { Spec = spec, Ai = ai, Death = death, Placement = placement });
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
            StopTicking();
        }

        // Lifecycle hooks (called by StateTracker).

        public static void OnBotJoined(IPlayer bot)
        {
            if (_bots.Any(b => b.PlayerId == bot.PlayerId)) return;

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

            _bots.Add(new BotController(bot, ai, p.Spec, p.Death, p.Placement));
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
                    ScheduleDeathKick(bot.PlayerId, spec, ai, policy, new BotPlacement(deathPos.Value, deathHeading), predecessorAi);
                    break;
            }
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
            CharacterTracker.Reset();
            MeleeProbe.Reset();
            CombatTracker.Reset();
            StopTicking();
            Logger.Log("BotManager reset.", LogLevel.DEBUG);
        }

        // Internals.

        private static void Untrack(int playerId)
        {
            int removed = _bots.RemoveAll(b => b.PlayerId == playerId);
            if (removed == 0) return;

            Logger.Log($"Bot {playerId} untracked. Active bots: {_bots.Count}.", LogLevel.INFO);
            if (_bots.Count == 0) StopTicking();
        }

        // A death-triggered kick is delayed so the game can credit the killer and play the death before
        // the bot is removed (an immediate kick makes the bot vanish without crediting the kill).
        // A non-null replacementSpec (+ position) spawns a replacement once the kick fires (Replace).
        private static void ScheduleDeathKick(int playerId, BotSpawnSpec replacementSpec, BotAiEnum ai, BotDeathPolicy death, BotPlacement? placement, IBotAi predecessor = null)
        {
            if (!UnityEngine.Application.isPlaying)
            {
                // Edit-mode (tests): no coroutine host / no real game - do it synchronously, no delays.
                KickBot(playerId);
                SpawnReplacement(replacementSpec, ai, death, placement, predecessor);
                return;
            }

            MonoBehaviourRunner.Instance.StartCoroutine(DeathKickRoutine(playerId, replacementSpec, ai, death, placement, predecessor));
        }

        private static IEnumerator DeathKickRoutine(int playerId, BotSpawnSpec replacementSpec, BotAiEnum ai, BotDeathPolicy death, BotPlacement? placement, IBotAi predecessor)
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
            SpawnReplacement(replacementSpec, ai, death, placement, predecessor);
        }

        private static void KickBot(int playerId)
        {
            // Untrack only now (not at death time) so the bot stays tracked through the delay.
            Untrack(playerId);
            CarbonPlayerCommands.Despawn(playerId);
            Logger.Log($"Bot {playerId} kicked; replacement (if any) in {ReplaceDelaySeconds}s.", LogLevel.DEBUG);
        }

        private static void SpawnReplacement(BotSpawnSpec replacementSpec, BotAiEnum ai, BotDeathPolicy death, BotPlacement? placement, IBotAi predecessor)
        {
            if (replacementSpec != null && placement.HasValue)
                SpawnBots(1, replacementSpec, ai, death, placement, predecessor);
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

                // One shared snapshot of all spawned players/bots per tick, so neighbour-aware steering
                // (separation, collision avoidance) sees everyone without an O(n) gather per bot.
                CharacterTracker.Refresh(TickInterval);

                foreach (var bot in _bots.ToList())
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
        }
    }
}
