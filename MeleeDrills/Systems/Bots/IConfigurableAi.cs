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
        // AI's state unchanged in that case. On success 'message' is usually empty, but may carry an advisory:
        // a lever can be set legitimately and still do nothing because the lever it depends on is off.
        bool TrySet(string name, string value, out string message);

        // The current levers and their values, for a 'rc bot cfg <id>' listing. 'inactive' names the lever that
        // is switched off and stopping this one from doing anything, or is null while the lever is live. Setting
        // an inactive lever is allowed, so this is how the listing tells you it will sit dormant until you turn
        // its dependency on.
        IEnumerable<(string name, string value, string inactive)> ListParams();
    }
}
