using MDS.Systems;

namespace MDS.Events
{
    // Sets the death policy of one or all tracked bots. Pass playerId = -1 to target all.
    // Parameters: (int playerId, BotDeathPolicy policy)
    public class SetBotDeathPolicyEvent : IEvent
    {
        public EventEnum EventName => EventEnum.SetBotDeathPolicy;

        public bool Validate(object[] parameters, out string errorMessage)
        {
            errorMessage = string.Empty;

            if (parameters.Length != 2 ||
                parameters[0] is not int ||
                parameters[1] is not BotDeathPolicy)
            {
                errorMessage = "Invalid parameters. Expected: (int playerId, BotDeathPolicy policy).";
                return false;
            }

            return true;
        }

        public void Trigger(object[] parameters)
        {
            int playerId = (int)parameters[0];
            var policy = (BotDeathPolicy)parameters[1];

            if (playerId == -1)
            {
                BotManager.SetDeathAll(policy);
                Logger.Log($"SetBotDeathPolicyEvent: set policy '{policy}' on all bots.", LogLevel.INFO);
            }
            else
            {
                bool ok = BotManager.SetDeath(playerId, policy);
                Logger.Log(ok
                    ? $"SetBotDeathPolicyEvent: set policy '{policy}' on bot {playerId}."
                    : $"SetBotDeathPolicyEvent: no tracked bot with id {playerId}.", LogLevel.INFO);
            }
        }
    }
}
