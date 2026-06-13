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
    }
}
