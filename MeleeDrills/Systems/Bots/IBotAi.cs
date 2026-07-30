namespace MDS.Systems
{
    // The AI: pure decision logic. Observes the world, returns an intent. Swappable per difficulty.
    // Implementations should be side-effect free (no console commands) so they stay unit-testable;
    // the BotController turns the returned intent into carbonPlayers commands.
    public interface IBotAi
    {
        BotAiEnum AiType { get; }

        // Decide what the bot should do this tick. 'self' exposes the bot's identity/position;
        // implementations may also read StateTracker for targets. deltaTime is seconds since last tick.
        BotIntent Decide(BotController self, float deltaTime);

        // Called on a Replace-policy replacement, with the AI of the bot it replaces, so a standing instruction
        // (e.g. a move order) survives the death instead of the replacement standing inert. Carry only that;
        // transient state (remembered positions, velocity estimates, wander drift) must be left fresh, because
        // the replacement is a new body at the death position and stale values would produce a bogus first tick.
        // 'previous' may be a different AI type, so check before casting.
        void InheritFrom(IBotAi previous);
    }
}
