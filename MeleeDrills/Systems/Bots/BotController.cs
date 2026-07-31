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
        public IBotAi Ai => _ai;                          // current AI instance (debug/manual control hooks)
        public BotSpawnSpec Spec { get; }                 // null for randomly-spawned bots
        public BotDeathPolicy DeathPolicy { get; private set; }
        public bool Initialized { get; private set; }
        public bool IsAwaitingKick { get; private set; }   // a death-kick is scheduled; ignore further deaths

        // Bots never set their own vertical aim, and forcing input rotation only pins the heading, so left alone
        // they drift into looking at the ground. Level pitch is re-asserted on this cadence, which is slow enough
        // next to the 20 Hz movement commands to cost nothing.
        private const float LevelPitchDegrees = 0f;
        private const float PitchRefreshSeconds = 5f;

        private IBotAi _ai;
        private BotPlacement? _pendingPlacement;          // position + optional facing, applied on first spawn
        private float _nextPitchRefresh;
        private readonly float _trackedAt = Time.realtimeSinceStartup;

        public BotController(IPlayer bot, IBotAi ai, BotSpawnSpec spec, BotDeathPolicy deathPolicy, BotPlacement? placement)
        {
            Bot = bot;
            _ai = ai;
            Spec = spec;
            DeathPolicy = deathPolicy;
            _pendingPlacement = placement;
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
        // and applies a pending placement (summon / replace): teleport + optional facing.
        public void OnSpawned()
        {
            if (!Initialized)
            {
                CarbonPlayerCommands.EnableInputControl(PlayerId);
                Initialized = true;
                Logger.Log($"Bot {PlayerId} initialized for input control.", LogLevel.DEBUG);
            }

            CarbonPlayerCommands.SetPitch(PlayerId, LevelPitchDegrees);
            _nextPitchRefresh = Time.realtimeSinceStartup + PitchRefreshSeconds;

            if (_pendingPlacement.HasValue)
            {
                BotPlacement placement = _pendingPlacement.Value;
                CarbonPlayerCommands.Teleport(PlayerId, placement.Position);
                if (placement.Heading.HasValue)
                    CarbonPlayerCommands.SetInputRotation(PlayerId, placement.Heading.Value);

                Logger.Log($"Bot {PlayerId} placed at {placement.Position}{(placement.Heading.HasValue ? $" facing {placement.Heading.Value:F0} deg" : "")}.", LogLevel.DEBUG);
                _pendingPlacement = null;
            }
        }

        public void Tick(float deltaTime)
        {
            if (!Initialized) return;

            // Hold the aim level. Only while actually spawned, so we don't command a corpse.
            float now = Time.realtimeSinceStartup;
            if (now >= _nextPitchRefresh && Bot.PlayerObject != null)
            {
                CarbonPlayerCommands.SetPitch(PlayerId, LevelPitchDegrees);
                _nextPitchRefresh = now + PitchRefreshSeconds;
            }

            BotIntent intent = _ai.Decide(this, deltaTime);
            ApplyIntent(intent);
        }

        // Live world position via the spawn GameObject, or null if not currently spawned.
        public Vector3? Position => Bot.PlayerObject != null ? Bot.PlayerObject.transform.position : (Vector3?)null;

        // Live facing (degrees from North) via the spawn GameObject, or null if not currently spawned.
        public float? Heading => Bot.PlayerObject != null ? Bot.PlayerObject.transform.eulerAngles.y : (float?)null;

        // Live planar pose (world XZ + heading) for movement behaviors; false if not currently spawned.
        public bool TryGetPose(out BotPose pose)
        {
            pose = default;
            if (Position == null || Heading == null) return false;

            Vector3 p = Position.Value;
            pose = new BotPose(new Vector2(p.x, p.z), Heading.Value);
            return true;
        }

        // A bot the game accepted (joined) but never actually spawned never becomes Initialized. After a
        // timeout such a bot is a "ghost" - tracked by us but not present in the world - and is dropped.
        // A note that I have no idea why this happens still...
        public bool IsUnspawnedGhost(float now, float timeoutSeconds) =>
            !Initialized && (now - _trackedAt) > timeoutSeconds;

        private void ApplyIntent(BotIntent intent)
        {
            if (intent.MoveAxis.HasValue)
                CarbonPlayerCommands.SetInputAxis(PlayerId, intent.MoveAxis.Value.x, intent.MoveAxis.Value.y);

            if (intent.LookHeading.HasValue)
                CarbonPlayerCommands.SetInputRotation(PlayerId, intent.LookHeading.Value);

            if (intent.Running.HasValue)
                CarbonPlayerCommands.SetRunning(PlayerId, intent.Running.Value);

            if (!string.IsNullOrEmpty(intent.Action))
                CarbonPlayerCommands.PerformAction(PlayerId, intent.Action);
        }
    }
}
