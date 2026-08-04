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

        // The spawn batch this bot belongs to, 0 for none. Bots summoned by one command share one id for life,
        // which is how SquadCoordinator can treat them as a formation before they have anyone to fight. A Replace
        // replacement inherits it, so a station survives its members being killed.
        public int GroupId { get; }
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

        public BotController(IPlayer bot, IBotAi ai, BotSpawnSpec spec, BotDeathPolicy deathPolicy, BotPlacement? placement, int groupId = 0)
        {
            Bot = bot;
            _ai = ai;
            Spec = spec;
            DeathPolicy = deathPolicy;
            _pendingPlacement = placement;
            GroupId = groupId;
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

            WarnIfSpawnedAsSomethingElse();

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

        // The game does not always spawn the bot we asked for: a full team or a class that has hit its cap gets
        // silently substituted, which is how a bot ordered onto one side turns up on the other, or as a cannoneer
        // when a guard was asked for. Nothing here can force it, so at least say so in the log rather than leaving
        // it to be discovered in the field.
        private void WarnIfSpawnedAsSomethingElse()
        {
            if (Spec == null || !Bot.Faction.HasValue) return;
            if (Bot.Faction.Value == Spec.Faction && Bot.PlayerClass == Spec.Class) return;

            Logger.Log($"Bot {PlayerId} spawned as {FactionTokens.DisplayName(Bot.Faction.Value)}/{Bot.PlayerClass}, "
                       + $"but {FactionTokens.DisplayName(Spec.Faction)}/{Spec.Class} was requested. The game substituted it, "
                       + "usually because that team is full or the class is capped.", LogLevel.WARNING);
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
