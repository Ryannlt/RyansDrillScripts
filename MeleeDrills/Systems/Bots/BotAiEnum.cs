namespace MDS.Systems
{
    // Identifies a bot AI. Resolved to an IBotAi instance via BotAiFactory.
    public enum BotAiEnum
    {
        None,          // Does nothing - stands in place.
        Manual,        // Manually driven via 'rc bot move' - test harness for movement behaviors.
        StabbingDummy, // Static training dummy: stands facing one way and stabs on a cadence (practice target).
        RiposteDummy,  // Melee, reactive: stands its ground, blocks, and only counters once provoked.
        DuelingEasy,   // Dueling difficulty: slow reactions - beatable.
        DuelingNormal, // Dueling difficulty: human reactions.
        Dueling        // Dueling difficulty: best reactions + fastest pacing (hardest). Base Dueling = passive until attacked, then fights the attacker to the death.
        // Dueling*/RiposteDummy are MeleeAi presets (capability-toggle bundles); StabbingDummy is the MeleeDummy class. See MeleeAi.
    }
}
