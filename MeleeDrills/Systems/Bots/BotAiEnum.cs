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
        Dueling,       // Dueling difficulty: best reactions + fastest pacing (hardest). Base Dueling = passive until attacked, then fights the attacker to the death.
        Guardian,      // Escorts the player it was summoned onto and fights whatever threatens them.
        GroupEasy,     // Group difficulty: slow reactions - beatable.
        GroupNormal,   // Group difficulty: human reactions.
        Group,         // Group difficulty: best reactions + fastest pacing (hardest). Drill station: bots summoned together wait where they were set up, all wake when any one is stabbed, back off to re-form, then fight as a formation and return to the post afterwards.
        Test           // Development sandbox: Dueling's levers plus whatever behaviour is being worked on.
        // Dueling*/Group*/RiposteDummy are MeleeAi presets (capability-toggle bundles); StabbingDummy is the MeleeDummy class. See MeleeAi.
    }
}
