using UnityEngine;

// Owns one bot: its identity (IPlayer), spawn spec, AI, and death policy, and drives the actuator
// each tick. Phase 0 applies the AI's intent directly; a BotStateMachine arrives in a later phase.

namespace MDS.Systems
{
    public class BotController
    {
        public IPlayer Bot { get; }
        public int PlayerId => Bot.PlayerId;
        public BotAiEnum AiType => _ai.AiType;
        public BotSpawnSpec Spec { get; }                 // null for randomly-spawned bots
        public BotDeathPolicy DeathPolicy { get; private set; }
        public bool Initialized { get; private set; }
        public bool IsAwaitingKick { get; private set; }   // a death-kick is scheduled; ignore further deaths

        private IBotAi _ai;
        private Vector3? _pendingTeleport;                // applied on first spawn (summon / return-to-death)

        public BotController(IPlayer bot, IBotAi ai, BotSpawnSpec spec, BotDeathPolicy deathPolicy, Vector3? spawnAt)
        {
            Bot = bot;
            _ai = ai;
            Spec = spec;
            DeathPolicy = deathPolicy;
            _pendingTeleport = spawnAt;
        }

        public void SetAi(IBotAi ai)
        {
            _ai = ai;
            Logger.Log($"Bot {PlayerId} AI set to {ai.AiType}.", LogLevel.INFO);
        }

        public void SetDeathPolicy(BotDeathPolicy policy)
        {
            DeathPolicy = policy;
            Logger.Log($"Bot {PlayerId} death policy set to {policy}.", LogLevel.INFO);
        }

        public void MarkAwaitingKick() => IsAwaitingKick = true;

        // Called when the bot spawns (GameObject available). Enables input control on the first spawn,
        // and applies a pending teleport (summon / return-to-death placement) if one is queued.
        public void OnSpawned()
        {
            if (!Initialized)
            {
                CarbonPlayerCommands.EnableInputControl(PlayerId);
                Initialized = true;
                Logger.Log($"Bot {PlayerId} initialized for input control.", LogLevel.DEBUG);
            }

            if (_pendingTeleport.HasValue)
            {
                CarbonPlayerCommands.Teleport(PlayerId, _pendingTeleport.Value);
                Logger.Log($"Bot {PlayerId} teleported to {_pendingTeleport.Value}.", LogLevel.DEBUG);
                _pendingTeleport = null;
            }
        }

        public void Tick(float deltaTime)
        {
            if (!Initialized) return;

            BotIntent intent = _ai.Decide(this, deltaTime);
            ApplyIntent(intent);
        }

        // Live world position via the spawn GameObject, or null if not currently spawned.
        public Vector3? Position => Bot.PlayerObject != null ? Bot.PlayerObject.transform.position : (Vector3?)null;

        private void ApplyIntent(BotIntent intent)
        {
            if (intent.MoveAxis.HasValue)
                CarbonPlayerCommands.SetInputAxis(PlayerId, intent.MoveAxis.Value.x, intent.MoveAxis.Value.y);

            if (intent.LookHeading.HasValue)
                CarbonPlayerCommands.SetInputRotation(PlayerId, intent.LookHeading.Value);

            if (intent.Running.HasValue)
                CarbonPlayerCommands.SetRunning(PlayerId, intent.Running.Value);
        }
    }
}
