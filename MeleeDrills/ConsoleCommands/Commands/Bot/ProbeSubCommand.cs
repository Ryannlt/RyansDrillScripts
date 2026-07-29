using MDS.Core;
using MDS.Systems;

namespace MDS.ConsoleCommands
{
    // rc bot probe <playerId|me> [on|off]
    // Dev tool: logs the target player's melee packet actions + hurt events to the server log so we can learn
    // the PlayerActions vocabulary + timings. Probes ANY player (human or bot); 'me' = the caller. With no
    // on/off it toggles. No gameplay effect. See MeleeProbe.
    public class ProbeSubCommand : IBotSubCommand
    {
        public BotCommandEnum SubCommandName => BotCommandEnum.Probe;

        public bool Validate(string[] args, out string errorMessage)
        {
            errorMessage = string.Empty;

            if (args.Length < 1)
            {
                errorMessage = "Usage: rc bot probe <playerId|me> [on|off]";
                return false;
            }

            if (!args[0].Equals("me", System.StringComparison.OrdinalIgnoreCase) && !int.TryParse(args[0], out _))
            {
                errorMessage = "Target must be a playerId or 'me'.";
                return false;
            }

            if (args.Length > 1 &&
                !args[1].Equals("on", System.StringComparison.OrdinalIgnoreCase) &&
                !args[1].Equals("off", System.StringComparison.OrdinalIgnoreCase))
            {
                errorMessage = "Second argument must be 'on' or 'off'.";
                return false;
            }

            return true;
        }

        public void Execute(int playerId, string[] args)
        {
            int targetId = args[0].Equals("me", System.StringComparison.OrdinalIgnoreCase) ? playerId : int.Parse(args[0]);

            bool on;
            if (args.Length > 1)
            {
                on = args[1].Equals("on", System.StringComparison.OrdinalIgnoreCase);
                MeleeProbe.Set(targetId, on);
            }
            else
            {
                on = MeleeProbe.Toggle(targetId);
            }

            string state = on ? "Probing" : "Stopped probing";
            CommandExecutor.ExecuteCommand($"serverAdmin privateMessage {playerId} {state} player {targetId}. Watch the server log while they attack/block.");
        }
    }
}
