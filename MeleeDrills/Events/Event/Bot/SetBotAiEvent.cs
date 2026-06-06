using MDS.Systems;

namespace MDS.Events
{
    // Sets the AI of one or all tracked bots. Pass playerId = -1 to target all.
    // Parameters: (int playerId, BotAiEnum ai)
    public class SetBotAiEvent : IEvent
    {
        public EventEnum EventName => EventEnum.SetBotAi;

        public bool Validate(object[] parameters, out string errorMessage)
        {
            errorMessage = string.Empty;

            if (parameters.Length != 2 ||
                parameters[0] is not int ||
                parameters[1] is not BotAiEnum ai)
            {
                errorMessage = "Invalid parameters. Expected: (int playerId, BotAiEnum ai).";
                return false;
            }

            if (!BotAiFactory.IsRegistered(ai))
            {
                errorMessage = $"AI type '{ai}' is not registered.";
                return false;
            }

            return true;
        }

        public void Trigger(object[] parameters)
        {
            int playerId = (int)parameters[0];
            var ai = (BotAiEnum)parameters[1];

            if (playerId == -1)
            {
                BotManager.SetAiAll(ai);
                Logger.Log($"SetBotAiEvent: set AI '{ai}' on all bots.", LogLevel.INFO);
            }
            else
            {
                bool ok = BotManager.SetAi(playerId, ai);
                Logger.Log(ok
                    ? $"SetBotAiEvent: set AI '{ai}' on bot {playerId}."
                    : $"SetBotAiEvent: no tracked bot with id {playerId}.", LogLevel.INFO);
            }
        }
    }
}
