using System.Collections.Generic;

namespace MDS.Systems
{
    // An IBotAi that exposes tunable "levers" by name, so a bot's behavior can be dialed in granularly rather
    // than via preset packages. Set per-bot with 'rc bot cfg <id> <lever> <value>'; the default for a lever
    // comes from GlobalAiConfigurable ('rc set globalAI <AiType> <lever> <value>'). Values are STRINGS the AI
    // parses itself, so a lever can be a float, an enum, whatever. See MeleeDummy for the first implementation.
    public interface IConfigurableAi
    {
        // Set one lever. Returns false with an error for an unknown name or an invalid value, and leaves the
        // AI's state unchanged in that case.
        bool TrySet(string name, string value, out string error);

        // The current levers and their values, for a 'rc bot cfg <id>' listing.
        IEnumerable<(string name, string value)> ListParams();
    }
}
