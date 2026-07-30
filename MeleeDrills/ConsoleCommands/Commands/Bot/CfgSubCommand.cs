using System.Text;
using MDS.Core;
using MDS.Systems;

namespace MDS.ConsoleCommands
{
    // rc bot cfg <target> [<lever> <value>]
    //   with a lever and value: set one behaviour lever on the matching bots (only those whose AI is configurable).
    //   without: list the matching bots' current levers and values.
    // <target> is a playerId, all, attacking, defending, or a faction name. This is a per-bot override; a lever's
    // default comes from 'rc set globalAI'. It mirrors the 'move' flow: resolve the target with BotTargetSelector,
    // apply to each fitting bot, and skip the rest.
    public class CfgSubCommand : IBotSubCommand
    {
        public BotCommandEnum SubCommandName => BotCommandEnum.Cfg;

        public bool Validate(string[] args, out string errorMessage)
        {
            errorMessage = string.Empty;

            if (args.Length < 1)
            {
                errorMessage = "Usage: rc bot cfg <target> [<lever> <value>]";
                return false;
            }

            if (!BotTargetSelector.IsValidToken(args[0]))
            {
                errorMessage = $"Invalid target '{args[0]}'. Use a playerId, all, attacking, defending, or a faction name.";
                return false;
            }

            if (args.Length == 2)
            {
                errorMessage = "Provide a value: rc bot cfg <target> <lever> <value> (or just <target> to list).";
                return false;
            }

            return true;
        }

        public void Execute(int playerId, string[] args)
        {
            if (args.Length >= 3) SetLever(playerId, args[0], args[1], args[2]);
            else ListLevers(playerId, args[0]);
        }

        private static void SetLever(int playerId, string target, string lever, string value)
        {
            int applied = 0, skipped = 0;
            string lastError = null;

            foreach (int id in BotTargetSelector.Resolve(target))
            {
                if (FindController(id)?.Ai is IConfigurableAi cfg)
                {
                    if (cfg.TrySet(lever, value, out string error)) applied++;
                    else { skipped++; lastError = error; }
                }
                else skipped++;
            }

            string msg = applied > 0
                ? $"Set '{lever}' = '{value}' on {applied} bot(s)." + (skipped > 0 ? $" ({skipped} skipped.)" : "")
                : lastError ?? $"No configurable bot matched '{target}'.";
            CommandExecutor.ExecuteCommand($"serverAdmin privateMessage {playerId} {msg}");
        }

        private static void ListLevers(int playerId, string target)
        {
            foreach (int id in BotTargetSelector.Resolve(target))
            {
                BotController controller = FindController(id);
                if (controller?.Ai is IConfigurableAi cfg)
                {
                    var sb = new StringBuilder($"Bot {id} ({controller.AiType}):");
                    foreach (var (name, val) in cfg.ListParams())
                        sb.Append($" {name}={val}");
                    CommandExecutor.ExecuteCommand($"serverAdmin privateMessage {playerId} {sb}");
                }
                else
                {
                    CommandExecutor.ExecuteCommand($"serverAdmin privateMessage {playerId} Bot {id} ({controller?.AiType}) has no configurable levers.");
                }
            }
        }

        private static BotController FindController(int id)
        {
            foreach (var c in BotManager.Bots)
                if (c.PlayerId == id) return c;
            return null;
        }
    }
}
