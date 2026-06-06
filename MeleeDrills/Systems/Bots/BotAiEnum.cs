namespace MDS.Systems
{
    // Identifies a bot AI. Resolved to an IBotAi instance via BotAiFactory.
    public enum BotAiEnum
    {
        Idle   // Does nothing - stands in place. (Phase 0)
        // Phase 1+: Dummy (static stabber), Facing, Melee, ...
    }
}
