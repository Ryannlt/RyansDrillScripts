using System;
using System.Collections.Generic;
using System.Linq;
using HoldfastSharedMethods;

// Resolves a bot-target token to the matching tracked bot playerIds. Shared by the bot sub-commands
// (and reusable by drills). Tokens:
//   <playerId> | all | attacking | defending | <FactionCountry name>
// 'attacking'/'defending' map to the round's factions via StateTracker. Faction matching uses each
// bot's CURRENT faction (Bot.Faction).

namespace MDS.Systems
{
    public static class BotTargetSelector
    {
        public static bool IsValidToken(string token)
        {
            if (string.IsNullOrEmpty(token)) return false;
            if (int.TryParse(token, out _)) return true;   // playerId

            string t = token.ToLowerInvariant();
            if (t == "all" || t == "attacking" || t == "defending") return true;

            return Enum.TryParse<FactionCountry>(token, true, out _);
        }

        // Returns the playerIds of tracked bots matching the token (empty if none match / token invalid).
        public static List<int> Resolve(string token)
        {
            var result = new List<int>();
            if (string.IsNullOrEmpty(token)) return result;

            if (int.TryParse(token, out int id))
            {
                if (BotManager.Bots.Any(b => b.PlayerId == id))
                    result.Add(id);
                return result;
            }

            string t = token.ToLowerInvariant();

            if (t == "all")
            {
                foreach (var b in BotManager.Bots)
                    result.Add(b.PlayerId);
                return result;
            }

            FactionCountry? faction = ResolveFaction(t, token);
            if (faction.HasValue)
            {
                foreach (var b in BotManager.Bots)
                    if (b.Bot.Faction == faction.Value)
                        result.Add(b.PlayerId);
            }

            return result;
        }

        private static FactionCountry? ResolveFaction(string lower, string original)
        {
            if (lower == "attacking") return StateTracker.AttackingFaction;
            if (lower == "defending") return StateTracker.DefendingFaction;
            if (Enum.TryParse(original, true, out FactionCountry parsed)) return parsed;
            return null;
        }
    }
}
