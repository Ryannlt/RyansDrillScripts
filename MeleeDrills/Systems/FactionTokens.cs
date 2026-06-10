using HoldfastSharedMethods;
using MDS.Core;

namespace MDS.Systems
{
    // Resolves a faction "token" to a concrete FactionCountry. A token is:
    //   attacking | defending | <FactionCountry name> (e.g. French)
    // attacking/defending map to the round's factions via StateTracker, so they must be resolved at the
    // moment the faction is needed (they swap per round). Shared by bot spawn args, line spawning, and
    // bot targeting. Numeric input is rejected (so it can't be mistaken for an enum's underlying value).
    public static class FactionTokens
    {
        public static bool IsToken(string token)
        {
            if (string.IsNullOrEmpty(token)) return false;
            if (token.Equals("attacking", System.StringComparison.OrdinalIgnoreCase)) return true;
            if (token.Equals("defending", System.StringComparison.OrdinalIgnoreCase)) return true;
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
            return EnumParser.TryParseEnumStrict(token, out faction);
        }
    }
}
