using UnityEngine;
using MDS.Systems;

namespace MDS.Events
{
    // Spawns one or more bots. The COMMAND layer resolves caller context (faction/class, position)
    // into explicit values; this event is caller-agnostic and reusable by drills.
    // Parameters: (BotSpawnSpec spec | null for random, int count, BotAiEnum ai, BotDeathPolicy death, Vector3? spawnAt)
    public class SpawnBotsEvent : IEvent
    {
        public EventEnum EventName => EventEnum.SpawnBots;

        public bool Validate(object[] parameters, out string errorMessage)
        {
            errorMessage = string.Empty;

            if (parameters.Length != 5 ||
                !(parameters[0] is null || parameters[0] is BotSpawnSpec) ||
                parameters[1] is not int count ||
                parameters[2] is not BotAiEnum ai ||
                parameters[3] is not BotDeathPolicy ||
                !(parameters[4] is null || parameters[4] is Vector3))
            {
                errorMessage = "Invalid parameters. Expected: (BotSpawnSpec|null, int count, BotAiEnum, BotDeathPolicy, Vector3? spawnAt).";
                return false;
            }

            if (count <= 0)
            {
                errorMessage = "Count must be greater than 0.";
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
            var spec = parameters[0] as BotSpawnSpec;
            int count = (int)parameters[1];
            var ai = (BotAiEnum)parameters[2];
            var death = (BotDeathPolicy)parameters[3];
            Vector3? spawnAt = parameters[4] as Vector3?;

            BotManager.SpawnBots(count, spec, ai, death, spawnAt);
            Logger.Log($"SpawnBotsEvent: {count}x {(spec == null ? "random" : $"{spec.Faction}/{spec.Class}")}, AI {ai}, death {death}.", LogLevel.INFO);
        }
    }
}
