using HoldfastSharedMethods;

namespace MDS.Systems
{
    // What a bot IS. Faction/Class are required for carbonPlayers spawnSpecific (resolved from the
    // caller when the admin omits them). Name/RegTag/UniformId are optional extras. Stored per bot so
    // a kick+replace death policy (Slice B) can recreate an identical bot.
    public class BotSpawnSpec
    {
        public FactionCountry Faction { get; }
        public PlayerClass Class { get; }
        public string Name { get; }        // null/empty => omitted
        public string RegTag { get; }      // null/empty => omitted
        public int? UniformId { get; }     // null => omitted

        public BotSpawnSpec(FactionCountry faction, PlayerClass playerClass, string name = null, string regTag = null, int? uniformId = null)
        {
            Faction = faction;
            Class = playerClass;
            Name = name;
            RegTag = regTag;
            UniformId = uniformId;
        }
    }
}
