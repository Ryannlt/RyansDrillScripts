using HoldfastGame;

// The only part of MDS that reads the game's own internals, kept to one tiny assembly on purpose.
//
// Why it exists: a whitelisted admin never produces a server-side mod callback. Server-side OnRCLogin is
// dispatched from exactly one place - ServerRemoteConsoleAccessManager.RequestLogin, the handler for someone
// typing a password. A whitelisted admin goes through AutoLoginAdministrator instead, which acks the client and
// calls OnAdminLoggedIn; that only broadcasts to clients and writes log lines, and never reaches mods. So MDS had
// to infer admin status from a client-side ping firing an rc command, which cannot work at all when MDS is
// deployed server-only.
//
// ServerRemoteConsoleAccessManager.IsLoggedIn answers directly and covers both routes: loggedOnPlayerIDs for a
// typed password, serverAdminPlayersList for a whitelist match on either PlayFabId or PlatformId.
//
// BUILD RULES - both are load-bearing, see GameAccess~/build.ps1:
//
//  1. No HoldfastGame type may appear in a field, property, parameter or return type - only inside method bodies.
//     UMod reflects over mod assemblies during its MainBuild stage, running inside the Unity editor, where
//     "Assembly-CSharp" is Unity's own project assembly and contains no game types. A game type in a member
//     signature fails the mod build with ReflectionTypeLoadException.
//
//  2. These sources live in a folder ending in "~", which Unity treats as hidden: never imported, never compiled
//     into Assembly-CSharp, never added to the .csproj, never shipped as mod content. That is what lets them sit
//     inside the git repo - which is the mod folder itself - while still being compiled separately against the
//     game assembly.

namespace MDS.Core
{
    public static class GameAccess
    {
        // Whether the game-side chain is reachable right now. Null off-server, and null between maps while the
        // server component tree is being rebuilt.
        public static bool Available
        {
            get
            {
                ServerComponentReferenceManager server = ServerComponentReferenceManager.ServerInstance;
                return server != null && server.serverRemoteConsoleAccessManager != null;
            }
        }

        // A Try pattern rather than a plain bool, so callers can tell "not an admin" from "cannot answer". Those
        // must not collapse into the same value: the second has to fall back to MDS's own tracking, and returning
        // a bare false would silently deny every admin whenever the chain is briefly unavailable.
        public static bool TryIsAdmin(int playerId, out bool isAdmin)
        {
            isAdmin = false;

            ServerComponentReferenceManager server = ServerComponentReferenceManager.ServerInstance;
            if (server == null) return false;

            ServerRemoteConsoleAccessManager remoteConsole = server.serverRemoteConsoleAccessManager;
            if (remoteConsole == null) return false;

            try
            {
                // IsLoggedIn reads serverGameManager.NetworkPlayerAuthenticationDetails, which is not populated
                // for an id mid-connect. Treated as "cannot answer" rather than "not an admin", for the reason
                // above.
                isAdmin = remoteConsole.IsLoggedIn(playerId);
                return true;
            }
            catch
            {
                isAdmin = false;
                return false;
            }
        }
    }
}
