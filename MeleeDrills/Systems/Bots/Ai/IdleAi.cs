namespace MDS.Systems
{
    // The "does nothing" AI (Phase 0). Emits no movement, look, or actions - the bot stands still.
    public class IdleAi : IBotAi
    {
        public BotAiEnum AiType => BotAiEnum.Idle;

        public BotIntent Decide(BotController self, float deltaTime)
        {
            return BotIntent.Idle;
        }
    }
}
