using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using MDS.Core;

// Central bot subsystem: spawns/tracks bots, assigns AI + death policy, and runs a single tick
// coroutine that lives only while >=1 bot is active. Bots are dropped each new round (mirrors
// StateTracker) and auto-clear on map change via assembly reload. Lifecycle hooks are called by
// StateTracker. The COMMAND layer resolves caller context; this layer takes explicit data only.

namespace MDS.Systems
{
    public static class BotManager
    {
        private const float TickInterval = 0.05f;                 // 20 Hz - tunable
        private const BotAiEnum DefaultAi = BotAiEnum.Idle;
        private const BotDeathPolicy DefaultDeath = BotDeathPolicy.None;

        private static readonly List<BotController> _bots = new();
        private static readonly Queue<PendingBotSpawn> _pending = new();
        private static Coroutine _tickRoutine;

        public static IReadOnlyList<BotController> Bots => _bots;

        // ---- Command surface ----

        // spec == null => fully random spawn (carbonPlayers spawn). spawnAt teleports each bot on spawn.
        public static void SpawnBots(int count, BotSpawnSpec spec, BotAiEnum ai, BotDeathPolicy death, Vector3? spawnAt)
        {
            for (int i = 0; i < count; i++)
                _pending.Enqueue(new PendingBotSpawn { Spec = spec, Ai = ai, Death = death, SpawnAt = spawnAt });

            if (spec == null)
            {
                CarbonPlayerCommands.Spawn(count);
            }
            else
            {
                for (int i = 0; i < count; i++)
                    CarbonPlayerCommands.SpawnSpecific(spec);
            }

            Logger.Log($"Requested {count} bot(s). Spec: {(spec == null ? "random" : $"{spec.Faction}/{spec.Class}")}, AI {ai}, death {death}.", LogLevel.INFO);
        }

        public static bool SetAi(int playerId, BotAiEnum ai)
        {
            var bot = _bots.FirstOrDefault(b => b.PlayerId == playerId);
            if (bot == null) return false;

            bot.SetAi(BotAiFactory.Create(ai));
            return true;
        }

        public static void SetAiAll(BotAiEnum ai)
        {
            foreach (var bot in _bots)
                bot.SetAi(BotAiFactory.Create(ai));
        }

        public static bool SetDeath(int playerId, BotDeathPolicy policy)
        {
            var bot = _bots.FirstOrDefault(b => b.PlayerId == playerId);
            if (bot == null) return false;

            bot.SetDeathPolicy(policy);
            return true;
        }

        public static void SetDeathAll(BotDeathPolicy policy)
        {
            foreach (var bot in _bots)
                bot.SetDeathPolicy(policy);
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

            _bots.Clear();
            _pending.Clear();
            StopTicking();
        }

        // ---- Lifecycle hooks (called by StateTracker) ----

        public static void OnBotJoined(IPlayer bot)
        {
            if (_bots.Any(b => b.PlayerId == bot.PlayerId)) return;

            PendingBotSpawn p = _pending.Count > 0
                ? _pending.Dequeue()
                : new PendingBotSpawn { Spec = null, Ai = DefaultAi, Death = DefaultDeath, SpawnAt = null };

            _bots.Add(new BotController(bot, BotAiFactory.Create(p.Ai), p.Spec, p.Death, p.SpawnAt));
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

            switch (controller.DeathPolicy)
            {
                case BotDeathPolicy.None:
                    Logger.Log($"Bot {bot.PlayerId} died (policy: None). No action taken.", LogLevel.DEBUG);
                    break;

                case BotDeathPolicy.Kick:
                    Logger.Log($"Bot {bot.PlayerId} died (policy: Kick). Kicking.", LogLevel.INFO);
                    Untrack(bot.PlayerId);
                    CarbonPlayerCommands.Despawn(bot.PlayerId);
                    break;

                case BotDeathPolicy.ReturnToDeath:
                    // Capture everything we need before untracking.
                    Vector3? deathPos = controller.Position;
                    BotSpawnSpec spec = controller.Spec;
                    BotAiEnum ai = controller.AiType;
                    BotDeathPolicy death = controller.DeathPolicy;

                    if (spec == null)
                    {
                        // Random-spawned bots have no spec to replay - kick only.
                        Logger.Log($"Bot {bot.PlayerId} died (policy: ReturnToDeath) but has no spawn spec (random bot). Kicking only.", LogLevel.WARNING);
                        Untrack(bot.PlayerId);
                        CarbonPlayerCommands.Despawn(bot.PlayerId);
                        break;
                    }

                    if (!deathPos.HasValue)
                    {
                        // Position unavailable (GameObject already gone) - kick only.
                        Logger.Log($"Bot {bot.PlayerId} died (policy: ReturnToDeath) but position unavailable. Kicking only.", LogLevel.WARNING);
                        Untrack(bot.PlayerId);
                        CarbonPlayerCommands.Despawn(bot.PlayerId);
                        break;
                    }

                    Logger.Log($"Bot {bot.PlayerId} died (policy: ReturnToDeath). Kicking and queuing replacement at {deathPos.Value}.", LogLevel.INFO);
                    Untrack(bot.PlayerId);
                    CarbonPlayerCommands.Despawn(bot.PlayerId);
                    SpawnBots(1, spec, ai, death, deathPos.Value);
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
            _bots.Clear();
            _pending.Clear();
            StopTicking();
            Logger.Log("BotManager reset.", LogLevel.DEBUG);
        }

        // ---- Internals ----

        private static void Untrack(int playerId)
        {
            int removed = _bots.RemoveAll(b => b.PlayerId == playerId);
            if (removed == 0) return;

            Logger.Log($"Bot {playerId} untracked. Active bots: {_bots.Count}.", LogLevel.INFO);
            if (_bots.Count == 0) StopTicking();
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
            while (_bots.Count > 0)
            {
                foreach (var bot in _bots.ToList())
                    bot.Tick(TickInterval);

                yield return new WaitForSeconds(TickInterval);
            }

            _tickRoutine = null;
        }

        private struct PendingBotSpawn
        {
            public BotSpawnSpec Spec;
            public BotAiEnum Ai;
            public BotDeathPolicy Death;
            public Vector3? SpawnAt;
        }
    }
}
