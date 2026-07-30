namespace MDS.Systems
{
    // Identifies a bot AI. Resolved to an IBotAi instance via BotAiFactory.
    public enum BotAiEnum
    {
        None,        // Does nothing - stands in place.
        Manual,      // Manually driven via 'rc bot move' - test harness for movement behaviors.
        MeleeDefend, // Melee combat, defensive: faces the nearest enemy and reactively blocks its attacks.
        MeleeFight,  // Melee combat, offensive: MeleeDefend plus a riposte during the enemy's recovery.
        MeleeDummy,  // Static training dummy: stands facing one way and stabs on a cadence (practice target).
        Sparring,    // Melee combat, reactive: stands its ground, blocks, and only counters once provoked.
        Guardian     // Melee combat: passive (block only) until attacked, then fights that attacker until it dies.
        // MeleeDefend/MeleeFight/Sparring/Guardian are all MeleeAi presets (capability-toggle bundles); see MeleeAi.
    }
}
