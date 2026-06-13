using HoldfastSharedMethods;

namespace MDS.Systems
{
    // A bot-line spec parsed from a SpawnLine config variable. Everything is resolved EXCEPT the faction,
    // which stays a token ("attacking" | "defending" | a FactionCountry name | an extension faction name
    // like ARBritish): attacking/defending swap per round, so the faction can only be resolved at spawn
    // time against the live round (via FactionTokens.TryResolve).
    public struct LineSpec
    {
        public int Count;
        public string FactionToken;   // attacking | defending | FactionCountry name | extension name
        public PlayerClass Class;
        public string Name;           // null/empty => omitted
        public string RegTag;         // null/empty => omitted
        public int? UniformId;        // null => omitted
        public BotAiEnum Ai;
        public BotDeathPolicy Death;
    }
}
