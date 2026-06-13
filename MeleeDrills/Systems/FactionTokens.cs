using System.Collections.Generic;
using HoldfastSharedMethods;
using MDS.Core;

namespace MDS.Systems
{
    // Resolves a faction "token" to a FactionCountry. A token is:
    //   attacking | defending | <FactionCountry name> (e.g. French) | <extension name> (e.g. ARBritish)
    // attacking/defending map to the round's factions via StateTracker, so they must be resolved at the
    // moment the faction is needed (they swap per round). Shared by bot spawn args, line spawning, and
    // bot targeting. Numeric input is rejected (so it can't be mistaken for an enum's underlying value).
    //
    // EXTENSION factions are ones the game added but the SDK's FactionCountry enum does not name yet. We
    // map their names to the integer ids the game uses (cast into FactionCountry). Because the SDK reports
    // those same integers back to us (OnRoundDetails / OnPlayerSpawned) and the spawn command accepts the
    // integer, treating them as ordinary FactionCountry values makes everything - spawning, attacking/
    // defending, targeting - just work. Add new game factions here until the SDK enum catches up.
    //
    // When a faction is valid but not one of the active round factions, TryResolve falls back to the
    // attacking faction and logs a warning, so callers never silently get a wrong (randomly-assigned) team.
    public static class FactionTokens
    {
        private static readonly Dictionary<string, FactionCountry> ExtensionFactions =
            new(System.StringComparer.OrdinalIgnoreCase)
            {
                { "ARBritish", (FactionCountry)11 },
                { "ARAmerican", (FactionCountry)12 },
            };

        public static bool IsToken(string token)
        {
            if (string.IsNullOrEmpty(token)) return false;
            if (token.Equals("attacking", System.StringComparison.OrdinalIgnoreCase)) return true;
            if (token.Equals("defending", System.StringComparison.OrdinalIgnoreCase)) return true;
            if (ExtensionFactions.ContainsKey(token)) return true;
            return EnumParser.TryParseEnumStrict(token, out FactionCountry _);
        }

        public static bool TryResolve(string token, out FactionCountry faction)
        {
            faction = default;
            if (string.IsNullOrEmpty(token)) return false;

            if (token.Equals("attacking", System.StringComparison.OrdinalIgnoreCase))
            {
                faction = StateTracker.AttackingFaction;
                return true;
            }
            if (token.Equals("defending", System.StringComparison.OrdinalIgnoreCase))
            {
                faction = StateTracker.DefendingFaction;
                return true;
            }

            // A named or extension faction. Either form gives a concrete FactionCountry value.
            if (!ExtensionFactions.TryGetValue(token, out faction) &&
                !EnumParser.TryParseEnumStrict(token, out faction))
                return false;

            // Valid, but maybe not active this round. If it's not one of the two active factions the game
            // assigns the bot arbitrarily, so fall back to attacking.
            if (!IsActive(faction))
            {
                Logger.Log($"Faction '{token}' is not active this round; defaulting to attacking ({DisplayName(StateTracker.AttackingFaction)}).", LogLevel.WARNING);
                faction = StateTracker.AttackingFaction;
            }
            return true;
        }

        // The human-readable faction name for logs/messages. Named FactionCountry values print as their
        // enum name; extension factions (no enum member) print their registered name instead of the bare
        // integer; anything else falls back to the integer.
        public static string DisplayName(FactionCountry faction)
        {
            if (System.Enum.IsDefined(typeof(FactionCountry), faction))
                return faction.ToString();

            foreach (var kvp in ExtensionFactions)
                if (kvp.Value == faction)
                    return kvp.Key;

            return faction.ToString();
        }

        private static bool IsActive(FactionCountry faction) =>
            faction == StateTracker.AttackingFaction || faction == StateTracker.DefendingFaction;
    }
}
