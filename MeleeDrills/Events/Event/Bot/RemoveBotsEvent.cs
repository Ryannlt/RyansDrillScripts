using MDS.Systems;

namespace MDS.Events
{
    // Removes one or all tracked bots. Pass playerId = -1 to remove all.
    // Parameters: (int playerId)
    public class RemoveBotsEvent : IEvent
    {
        public EventEnum EventName => EventEnum.RemoveBots;

        public bool Validate(object[] parameters, out string errorMessage)
        {
            errorMessage = string.Empty;

            if (parameters.Length != 1 || parameters[0] is not int)
            {
                errorMessage = "Invalid parameters. Expected: (int playerId), or -1 to remove all.";
                return false;
            }

            return true;
        }

        public void Trigger(object[] parameters)
        {
            int playerId = (int)parameters[0];

            if (playerId == -1)
            {
                BotManager.RemoveAll();
                Logger.Log("RemoveBotsEvent triggered: removed all bots.", LogLevel.INFO);
            }
            else
            {
                bool removed = BotManager.RemoveBot(playerId);
                Logger.Log(removed
                    ? $"RemoveBotsEvent triggered: removed bot {playerId}."
                    : $"RemoveBotsEvent: no tracked bot with id {playerId}.", LogLevel.INFO);
            }
        }
    }
}
