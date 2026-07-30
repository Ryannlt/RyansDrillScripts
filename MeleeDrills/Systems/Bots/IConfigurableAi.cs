using System.Collections.Generic;

namespace MDS.Systems
{
    // An IBotAi that exposes tunable "levers" by name, so a bot's behaviour can be dialled in per lever rather
    // than only through preset packages. Set them per bot with 'rc bot cfg <id> <lever> <value>'; a lever's
    // default comes from GlobalAiConfigurable. Values are strings the AI parses itself, so a lever can be a
    // float, an enum, or anything else. See MeleeDummy for the first implementation.
    public interface IConfigurableAi
    {
        // Set one lever. Returns false with an error for an unknown name or an invalid value, and leaves the
        // AI's state unchanged in that case.
        bool TrySet(string name, string value, out string error);

        // The current levers and their values, for a 'rc bot cfg <id>' listing.
        IEnumerable<(string name, string value)> ListParams();
    }
}
