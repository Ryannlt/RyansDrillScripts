namespace MDS.Systems
{
    // Identifies a bot AI. Resolved to an IBotAi instance via BotAiFactory.
    public enum BotAiEnum
    {
        None,   // Does nothing - stands in place.
        Manual  // Manually driven via 'rc bot move' - test harness for movement behaviors.
        // Phase 1+: Dummy (static stabber), Facing, Melee, ...
    }
}
