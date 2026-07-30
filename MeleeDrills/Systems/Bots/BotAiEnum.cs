namespace MDS.Systems
{
    // Identifies a bot AI. Resolved to an IBotAi instance via BotAiFactory.
    public enum BotAiEnum
    {
        None,          // Does nothing - stands in place.
        Manual,        // Manually driven via 'rc bot move' - test harness for movement behaviors.
        StabbingDummy, // Static training dummy: stands facing one way and stabs on a cadence (practice target).
        RiposteDummy,  // Melee, reactive: stands its ground, blocks, and only counters once provoked.
        Dueling        // Melee: passive (block only) until attacked, then fights that attacker until it dies.
        // RiposteDummy/Dueling are MeleeAi presets (capability-toggle bundles); StabbingDummy is MeleeDummy. See MeleeAi.
    }
}
