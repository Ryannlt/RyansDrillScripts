using System.Globalization;
using UnityEngine;
using MDS.Core;
using MDS.Events;
using MDS.Systems;

namespace MDS.ConsoleCommands
{
    // rc bot move <bots> <behavior> [args]
    //   seek      <x z | playerId | me>   - run toward a point/player, facing the direction of travel
    //   arrive    <x z | playerId | me>   - like seek, but decelerate to a smooth stop at the destination
    //   flee      <x z | playerId | me>   - run directly away from a point/player
    //   facepoint <x z | playerId | me>   - rotate in place to face a point/player
    //   face      <deg>                   - rotate in place to a heading (degrees from North)
    //   stop                              - halt movement
    // <bots> selects WHICH bots get the order (playerId|all|attacking|defending|faction). A player/me
    // DESTINATION is tracked live each tick (chase / flee a moving player). Orders only apply to bots
    // already on the Manual AI ('rc bot setBotAi <bots> Manual'); bots on other AIs are left untouched.
    public class MoveSubCommand : IBotSubCommand
    {
        public BotCommandEnum SubCommandName => BotCommandEnum.Move;

        // Validate has no caller id; 'me' is structurally valid here and resolved to the real caller in Execute.
        public bool Validate(string[] args, out string errorMessage) =>
            TryParseOrder(args, callerId: 0, out _, out errorMessage);

        public void Execute(int playerId, string[] args)
        {
            if (!TryParseOrder(args, playerId, out MoveOrder order, out string error))
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
            "Usage: rc bot move <bots> <seek|arrive|flee|facepoint <x z|playerId|me> | face <deg> | stop>";

        private static bool TryParseOrder(string[] args, int callerId, out MoveOrder order, out string error)
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
                    if (args.Length != 2) { error = "Usage: rc bot move <bots> stop"; return false; }
                    order = MoveOrder.Stop();
                    return true;

                case "face":
                    if (args.Length != 3 || !TryFloat(args[2], out float deg))
                    {
                        error = "Usage: rc bot move <bots> face <deg>";
                        return false;
                    }
                    order = MoveOrder.Face(deg);
                    return true;

                case "seek":
                case "arrive":
                case "flee":
                case "facepoint":
                    if (!TryParseDestination(args, callerId, out MoveTarget target, out error))
                        return false;
                    order = behavior switch
                    {
                        "seek" => MoveOrder.Seek(target),
                        "arrive" => MoveOrder.Arrive(target),
                        "flee" => MoveOrder.Flee(target),
                        _ => MoveOrder.FacePoint(target),
                    };
                    return true;

                default:
                    error = $"Unknown behavior '{args[1]}'. Valid: seek, arrive, flee, face, facepoint, stop.";
                    return false;
            }
        }

        // Destination tokens after the behavior: "x z" (two numbers) | "<playerId>" (one int) | "me" (caller).
        private static bool TryParseDestination(string[] args, int callerId, out MoveTarget target, out string error)
        {
            target = default;
            error = string.Empty;

            int rem = args.Length - 2; // tokens following the behavior keyword
            if (rem == 1)
            {
                string tok = args[2];
                if (tok.Equals("me", System.StringComparison.OrdinalIgnoreCase))
                {
                    target = MoveTarget.Player(callerId);
                    return true;
                }
                if (int.TryParse(tok, out int id))
                {
                    target = MoveTarget.Player(id);
                    return true;
                }
                error = "Destination must be 'x z', a playerId, or 'me'.";
                return false;
            }

            if (rem == 2)
            {
                if (!TryFloat(args[2], out float x) || !TryFloat(args[3], out float z))
                {
                    error = "Destination 'x z' must be two numbers.";
                    return false;
                }
                target = MoveTarget.At(new Vector2(x, z));
                return true;
            }

            error = "Usage: rc bot move <bots> <seek|arrive|flee|facepoint> <x z | playerId | me>";
            return false;
        }

        private static bool TryFloat(string s, out float value) =>
            float.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out value);
    }
}
