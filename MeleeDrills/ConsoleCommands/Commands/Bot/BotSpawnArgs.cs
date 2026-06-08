using System;
using HoldfastSharedMethods;
using MDS.ConfigVariables;
using MDS.Core;
using MDS.Systems;

namespace MDS.ConsoleCommands
{
    // Parses the shared positional spawn grammar used by 'spawn' and 'summon':
    //   [count] [faction class] [ai] [death] [name [regtag [uniformId]]]
    // - faction/class: provide both or neither; omitted => default to the caller's faction/class.
    // - ai/death: omitted => config defaults (botDefaultAi / botDefaultDeathPolicy); provide inline to override.
    // - name/regtag/uniformId: require an explicit faction+class.
    // Strict positional order:
    //   [count] [faction [class]] [ai] [death] [name [regtag [uniformId]]]
    // - Neither faction nor class   -> both default to caller's.
    // - Faction only (no class)     -> faction explicit, class defaults to caller's.
    // - Both faction and class      -> both explicit.
    // The ai and death slots, if present, MUST parse as a valid AI / death policy - a wrong token
    // errors rather than being silently taken as a name.
    public class BotSpawnArgs
    {
        public int Count { get; private set; } = 1;
        public BotSpawnSpec Spec { get; private set; }
        public BotAiEnum Ai { get; private set; }
        public BotDeathPolicy Death { get; private set; }

        // Structural validation only (no caller resolution); used by Validate which has no playerId.
        // allowCount: spawn accepts a leading count; summon does not (it's always a single bot).
        public static bool ValidateShape(string[] args, bool allowCount, out string error) =>
            ParseTokens(args, allowCount, out _, out error);

        // Full resolution incl. caller defaults; used by Execute which has the caller's playerId.
        public static bool TryResolve(string[] args, int callerPlayerId, bool allowCount, out BotSpawnArgs result, out string error)
        {
            result = null;

            if (!ParseTokens(args, allowCount, out Parsed p, out error))
                return false;

            FactionCountry faction;
            PlayerClass playerClass;

            var caller = StateTracker.GetPlayerById(callerPlayerId);

            if (p.HasSpec)
            {
                // Both explicit.
                faction = p.Faction;
                playerClass = p.Class;
            }
            else if (p.HasFaction)
            {
                // Faction explicit, class from caller.
                faction = p.Faction;
                if (caller?.PlayerClass == null)
                {
                    error = "You have no class to copy. Specify one explicitly: rc bot spawn [count] <faction> <class>";
                    return false;
                }
                playerClass = caller.PlayerClass.Value;
            }
            else
            {
                // Both from caller.
                if (caller?.Faction == null || caller.PlayerClass == null)
                {
                    error = "You have no faction/class to copy. Specify them explicitly: rc bot spawn [count] <faction> [class]";
                    return false;
                }
                faction = caller.Faction.Value;
                playerClass = caller.PlayerClass.Value;
            }

            result = new BotSpawnArgs
            {
                Count = p.Count,
                Spec = new BotSpawnSpec(faction, playerClass, p.Name, p.RegTag, p.UniformId),
                Ai = p.Ai ?? ((BotDefaultAiConfigurable)ConfigurableRegistry.Get(ConfigurableEnum.BotDefaultAi)).DefaultAi,
                Death = p.Death ?? ((BotDefaultDeathConfigurable)ConfigurableRegistry.Get(ConfigurableEnum.BotDefaultDeathPolicy)).DefaultPolicy
            };
            return true;
        }

        private static bool ParseTokens(string[] args, bool allowCount, out Parsed p, out string error)
        {
            p = new Parsed { Count = 1 };
            error = string.Empty;

            string order = allowCount
                ? "[count] [faction class] [ai] [death] [name [regtag [uniformId]]]"
                : "[faction class] [ai] [death] [name [regtag [uniformId]]]";

            int i = 0;

            // optional leading count (spawn only)
            if (allowCount && i < args.Length && int.TryParse(args[i], out int count))
            {
                if (count <= 0) { error = "Count must be a positive integer."; return false; }
                p.Count = count;
                i++;
            }

            // optional faction (and optional class)
            if (i < args.Length && EnumParser.TryParseEnumStrict(args[i], out FactionCountry faction))
            {
                p.Faction = faction;
                p.HasFaction = true;
                i++;

                // class is optional after faction - if the next token parses as one, consume it
                if (i < args.Length && EnumParser.TryParseEnumStrict(args[i], out PlayerClass playerClass))
                {
                    p.Class = playerClass;
                    p.HasSpec = true;
                    i++;
                }
            }

            // ai slot - if a token is present here it MUST be a valid AI (no silent fallthrough to name).
            if (i < args.Length)
            {
                if (!EnumParser.TryParseEnumStrict(args[i], out BotAiEnum ai))
                {
                    error = $"Unknown AI '{args[i]}'. Valid: {string.Join(", ", Enum.GetNames(typeof(BotAiEnum)))}. Order: {order}.";
                    return false;
                }
                p.Ai = ai;
                i++;
            }

            // death slot - if a token is present here it MUST be a valid death policy.
            if (i < args.Length)
            {
                if (!EnumParser.TryParseEnumStrict(args[i], out BotDeathPolicy death))
                {
                    error = $"Unknown death policy '{args[i]}'. Valid: {string.Join(", ", Enum.GetNames(typeof(BotDeathPolicy)))}. Order: {order}.";
                    return false;
                }
                p.Death = death;
                i++;
            }

            // optional name / regtag / uniformId (require at least an explicit faction)
            if (i < args.Length)
            {
                if (!p.HasFaction)
                {
                    error = $"Unexpected argument '{args[i]}'. Order: {order}.";
                    return false;
                }

                p.Name = args[i++];
                if (i < args.Length) p.RegTag = args[i++];
                if (i < args.Length)
                {
                    if (!int.TryParse(args[i], out int uniformId))
                    {
                        error = $"Invalid uniformId '{args[i]}'. Must be an integer.";
                        return false;
                    }
                    p.UniformId = uniformId;
                    i++;
                }
                if (i < args.Length)
                {
                    error = $"Too many arguments. Order: {order}.";
                    return false;
                }
            }

            return true;
        }

        private struct Parsed
        {
            public int Count;
            public FactionCountry Faction;
            public PlayerClass Class;
            public bool HasFaction;  // faction was explicitly provided
            public bool HasSpec;     // both faction AND class were explicitly provided
            public BotAiEnum? Ai;
            public BotDeathPolicy? Death;
            public string Name;
            public string RegTag;
            public int? UniformId;
        }
    }
}
