using System.Collections.Generic;
using UnityEngine;
using MDS.ConfigVariables;

namespace MDS.Systems
{
    // The StabbingDummy AI: a static training dummy that stands where it spawned, keeps its spawn facing, and
    // throws a stab on a steady cadence for a player to walk up to and practice blocking/attacking against. No
    // perception, targeting, or movement - the opposite of MeleeAi. Set with 'rc bot setBotAi <id> StabbingDummy';
    // aim it by facing the way you want when you summon it. (The class stays MeleeDummy; the AI name is StabbingDummy.)
    //
    // First CONFIGURABLE ai: its two levers (stabInterval, stabDirection) are settable per-bot with
    // 'rc bot cfg <id> <lever> <value>', defaulting from GlobalAiConfigurable ('rc set globalAI StabbingDummy <lever> ...').
    //
    // Reuses the confirmed strike mechanics (single MeleeStrike self-holds the windup, one ExecuteMeleeWeaponStrike
    // releases it cleanly, and a committed stab occupies ~1.5s before it can throw again) with its own tiny loop,
    // so the working combat AI (MeleeAi) is left untouched.
    public class MeleeDummy : IBotAi, IConfigurableAi
    {
        public enum StabDirection { Random, High, Low, Alternate }

        private const float WindupSeconds = 0.15f;   // hold the windup this long (one MeleeStrike) before releasing
        private const float FirstStabDelay = 1.0f;   // settle after spawning before the first stab

        // Built-in lever defaults (name -> value), the single source for these values: the constructor uses them
        // as its fallback, and GlobalAiConfigurable seeds its global defaults from here so
        // 'rc get globalAI StabbingDummy <lever>' reports a real value instead of "not set".
        public static readonly (string name, string value)[] DefaultLevers =
        {
            ("stabInterval", "1.7"),    // release -> next windup, seconds (>~1.5s stab recovery + a beat)
            ("stabDirection", "Random"),
        };

        // Tunable levers (see IConfigurableAi). Seeded from global defaults in the constructor.
        private float _stabInterval;                 // release -> next windup, seconds
        private StabDirection _stabDirection;        // which way each stab goes

        private bool _stancePending = true;   // issue EnableCombatStance once, on the first spawned tick
        private bool _windingUp;              // a stab is chambered, waiting to release
        private float _executeAt;             // realtime to release the current windup
        private float _nextWindupAt;          // realtime the next stab may begin
        private bool _lastHigh;               // toggles for StabDirection.Alternate

        public BotAiEnum AiType => BotAiEnum.StabbingDummy;

        public MeleeDummy()
        {
            // Each lever = its global default (settable) or the built-in fallback; TrySet does the typed
            // parse/validate. A garbage global default just falls back to the built-in.
            string ai = AiType.ToString();
            foreach (var (name, builtin) in DefaultLevers)
                if (!TrySet(name, GlobalAiConfigurable.Default(ai, name, builtin), out _))
                    TrySet(name, builtin, out _);
        }

        public BotIntent Decide(BotController self, float deltaTime)
        {
            if (!self.TryGetPose(out _)) return BotIntent.Idle; // not currently spawned - issue nothing

            float now = Time.realtimeSinceStartup;

            // One-time: enter combat stance so the dummy can strike. Consumed only once actually spawned.
            if (_stancePending)
            {
                _stancePending = false;
                _nextWindupAt = now + FirstStabDelay;
                return new BotIntent { Action = "EnableCombatStance", MoveAxis = Vector2.zero };
            }

            // Stand still. We never send a look command - the engine holds the spawn facing set in OnSpawned, so
            // the dummy keeps looking one way and its stabs go straight ahead.
            BotIntent intent = new BotIntent { MoveAxis = Vector2.zero };

            if (_windingUp)
            {
                if (now >= _executeAt)
                {
                    intent.Action = "ExecuteMeleeWeaponStrike"; // release; a single Execute ends the swing cleanly
                    _windingUp = false;
                    _nextWindupAt = now + _stabInterval;
                }
                // else: hold the windup silently (re-sending MeleeStrike would restart the windup animation)
            }
            else if (now >= _nextWindupAt)
            {
                _executeAt = now + WindupSeconds;
                _windingUp = true;
                intent.Action = "MeleeStrike" + NextStabDir(); // one windup command; do not repeat it
            }

            return intent;
        }

        // "High"/"Low" for the next stab, per the configured direction.
        private string NextStabDir()
        {
            switch (_stabDirection)
            {
                case StabDirection.High: return "High";
                case StabDirection.Low:  return "Low";
                case StabDirection.Alternate: _lastHigh = !_lastHigh; return _lastHigh ? "High" : "Low";
                default: return Random.value < 0.5f ? "High" : "Low"; // Random
            }
        }

        // ---- IConfigurableAi ----

        public bool TrySet(string name, string value, out string error)
        {
            error = string.Empty;
            switch (name.ToLowerInvariant())
            {
                case "stabinterval":
                    if (!float.TryParse(value, out float interval) || interval <= 0f)
                    {
                        error = "stabInterval must be a number > 0 (seconds between stabs).";
                        return false;
                    }
                    _stabInterval = interval;
                    return true;

                case "stabdirection":
                    if (!System.Enum.TryParse(value, true, out StabDirection dir))
                    {
                        error = "stabDirection must be one of: Random, High, Low, Alternate.";
                        return false;
                    }
                    _stabDirection = dir;
                    return true;

                default:
                    error = $"Unknown lever '{name}'. StabbingDummy levers: stabInterval, stabDirection.";
                    return false;
            }
        }

        public IEnumerable<(string name, string value)> ListParams()
        {
            yield return ("stabInterval", _stabInterval.ToString("0.##"));
            yield return ("stabDirection", _stabDirection.ToString());
        }

        // Carry the dummy's config to a Replace-policy replacement so its tuning isn't lost on death.
        public void InheritFrom(IBotAi previous)
        {
            if (previous is MeleeDummy d)
            {
                _stabInterval = d._stabInterval;
                _stabDirection = d._stabDirection;
            }
        }
    }
}
