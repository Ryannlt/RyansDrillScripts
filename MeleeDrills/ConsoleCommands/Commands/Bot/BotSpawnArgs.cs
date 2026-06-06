using HoldfastSharedMethods;
using MDS.ConfigVariables;
using MDS.Core;
using MDS.Systems;

namespace MDS.ConsoleCommands
{
    // Parses the shared positional spawn grammar used by 'spawn' and 'summon':
    //   [count] [faction class] [ai] [death] [name [regtag [uniformId]]]
    // - faction/class: provide both or neither; omitted => default to the caller's faction/class.
    // - ai/death: omitted => config defaults (botDefaultAi / botDefaultDeath); provide inline to override.
    // - name/regtag/uniformId: require an explicit faction+class.
    // Tokens are type-directed (faction/ai/death are recognised by parsing them), so e.g.
    // 'spawn 3 Idle' sets the AI while keeping the caller's faction/class. The admin is expected to
    // know the ordering; a wrong token produces a clear error message.
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

            if (p.HasSpec)
            {
                faction = p.Faction;
                playerClass = p.Class;
            }
            else
            {
                var caller = StateTracker.GetPlayerById(callerPlayerId);
                if (caller?.Faction == null || caller.PlayerClass == null)
                {
                    error = "You have no faction/class to copy. Specify them explicitly: rc bot spawn [count] <faction> <class>";
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

            // optional faction + class (all or none)
            if (i < args.Length && EnumParser.TryParseEnumStrict(args[i], out FactionCountry faction))
            {
                i++;
                if (i >= args.Length || !EnumParser.TryParseEnumStrict(args[i], out PlayerClass playerClass))
                {
                    error = $"Faction '{faction}' must be followed by a class.";
                    return false;
                }
                p.Faction = faction;
                p.Class = playerClass;
                p.HasSpec = true;
                i++;
            }

            // optional ai
            if (i < args.Length && EnumParser.TryParseEnumStrict(args[i], out BotAiEnum ai))
            {
                p.Ai = ai;
                i++;
            }

            // optional death
            if (i < args.Length && EnumParser.TryParseEnumStrict(args[i], out BotDeathPolicy death))
            {
                p.Death = death;
                i++;
            }

            // optional name / regtag / uniformId (require an explicit faction+class)
            if (i < args.Length)
            {
                if (!p.HasSpec)
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
            public bool HasSpec;
            public BotAiEnum? Ai;
            public BotDeathPolicy? Death;
            public string Name;
            public string RegTag;
            public int? UniformId;
        }
    }
}
