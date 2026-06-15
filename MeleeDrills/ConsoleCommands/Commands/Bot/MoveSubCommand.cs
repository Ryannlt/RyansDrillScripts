using System.Globalization;
using UnityEngine;
using MDS.Core;
using MDS.Events;
using MDS.Systems;

namespace MDS.ConsoleCommands
{
    // rc bot move <target> <seek|face|facepoint|stop> [args]
    //   seek <x> <z>       - walk/run toward a world point, facing the direction of travel
    //   facepoint <x> <z>  - rotate in place to face a world point
    //   face <deg>         - rotate in place to a heading (degrees from North)
    //   stop               - halt movement
    // Sets a movement order ONLY on bots already running the Manual AI (set via 'rc bot setBotAi <t> Manual').
    // Bots on any other AI are left untouched - this never reassigns AI. A test harness for movement behaviors.
    public class MoveSubCommand : IBotSubCommand
    {
        public BotCommandEnum SubCommandName => BotCommandEnum.Move;

        public bool Validate(string[] args, out string errorMessage) =>
            TryParseOrder(args, out _, out errorMessage);

        public void Execute(int playerId, string[] args)
        {
            if (!TryParseOrder(args, out MoveOrder order, out string error))
            {
                CommandExecutor.ExecuteCommand($"serverAdmin privateMessage {playerId} {error}");
                return;
            }

            bool success = EventDispatcher.Trigger(EventEnum.SetBotMoveOrder,
                new object[] { args[0], order }, out string eventError);

            if (!success)
            {
                Logger.Log($"Move failed: {eventError}", LogLevel.WARNING);
                CommandExecutor.ExecuteCommand($"serverAdmin privateMessage {playerId} {eventError}");
                return;
            }

            CommandExecutor.ExecuteCommand($"serverAdmin privateMessage {playerId} Move order '{order.Kind}' sent to Manual-AI bots matching '{args[0]}'. (Bots on other AIs are unaffected.)");
        }

        private const string Usage =
            "Usage: rc bot move <target> <seek x z | facepoint x z | face deg | stop>";

        private static bool TryParseOrder(string[] args, out MoveOrder order, out string error)
        {
            order = default;
            error = string.Empty;

            if (args.Length < 2) { error = Usage; return false; }

            if (!BotTargetSelector.IsValidToken(args[0]))
            {
                error = $"Invalid target '{args[0]}'. Use a playerId, all, attacking, defending, or a faction name.";
                return false;
            }

            string behavior = args[1].ToLowerInvariant();
            switch (behavior)
            {
                case "stop":
                    if (args.Length != 2) { error = "Usage: rc bot move <target> stop"; return false; }
                    order = MoveOrder.Stop();
                    return true;

                case "face":
                    if (args.Length != 3 || !TryFloat(args[2], out float deg))
                    {
                        error = "Usage: rc bot move <target> face <deg>";
                        return false;
                    }
                    order = MoveOrder.Face(deg);
                    return true;

                case "seek":
                case "facepoint":
                    if (args.Length != 4 || !TryFloat(args[2], out float x) || !TryFloat(args[3], out float z))
                    {
                        error = $"Usage: rc bot move <target> {behavior} <x> <z>";
                        return false;
                    }
                    Vector2 point = new Vector2(x, z);
                    order = behavior == "seek" ? MoveOrder.Seek(point) : MoveOrder.FacePoint(point);
                    return true;

                default:
                    error = $"Unknown behavior '{args[1]}'. Valid: seek, face, facepoint, stop.";
                    return false;
            }
        }

        private static bool TryFloat(string s, out float value) =>
            float.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out value);
    }
}
