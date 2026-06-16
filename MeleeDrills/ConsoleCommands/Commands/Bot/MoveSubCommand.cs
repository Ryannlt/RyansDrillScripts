using System.Globalization;
using UnityEngine;
using MDS.Core;
using MDS.Events;
using MDS.Systems;

namespace MDS.ConsoleCommands
{
    // rc bot move <bots> <behavior> [args]
    //   seek      <dest> [facing <dest>]  - run toward a point/player; optionally face a SEPARATE target
    //   arrive    <dest> [facing <dest>]  - like seek, but decelerate to a smooth stop at the destination
    //   flee      <dest> [facing <dest>]  - run directly away; e.g. 'flee me facing me' = backpedal
    //   facepoint <dest>                  - rotate in place to face a point/player
    //   face      <deg>                   - rotate in place to a heading (degrees from North)
    //   wander                            - roam continuously with gentle random turns
    //   stop                              - halt movement
    // <dest> = 'x z' | <playerId> | me. A player/me dest is tracked live each tick (chase/flee/face a
    // moving player). The optional 'facing <dest>' DECOUPLES facing from travel (default: face travel).
    // <bots> selects WHICH bots get the order (playerId|all|attacking|defending|faction). Orders only apply
    // to bots already on the Manual AI ('rc bot setBotAi <bots> Manual'); bots on other AIs are untouched.
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
            "Usage: rc bot move <bots> <behavior>. Behaviors: seek/arrive/flee <dest> [facing <dest>], " +
            "facepoint <dest>, face <deg>, wander, stop. dest = 'x z' | playerId | me.";

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

                case "wander":
                    if (args.Length != 2) { error = "Usage: rc bot move <bots> wander"; return false; }
                    order = MoveOrder.Wander();
                    return true;

                case "face":
                    if (args.Length != 3 || !TryFloat(args[2], out float deg))
                    {
                        error = "Usage: rc bot move <bots> face <deg>";
                        return false;
                    }
                    order = MoveOrder.Face(deg);
                    return true;

                case "facepoint":
                    if (!TryParseTargetTokens(args[2..], callerId, out MoveTarget facePoint, out error))
                        return false;
                    order = MoveOrder.FacePoint(facePoint);
                    return true;

                case "seek":
                case "arrive":
                case "flee":
                {
                    string[] tail = args[2..];
                    string[] destTokens = tail;
                    MoveTarget? facing = null;

                    int fi = IndexOfFacing(tail);
                    if (fi >= 0)
                    {
                        destTokens = tail[..fi];
                        if (!TryParseTargetTokens(tail[(fi + 1)..], callerId, out MoveTarget f, out error))
                            return false;
                        facing = f;
                    }

                    if (!TryParseTargetTokens(destTokens, callerId, out MoveTarget dest, out error))
                        return false;

                    order = behavior switch
                    {
                        "seek" => MoveOrder.Seek(dest),
                        "arrive" => MoveOrder.Arrive(dest),
                        _ => MoveOrder.Flee(dest),
                    };
                    order.FaceTarget = facing;
                    return true;
                }

                default:
                    error = $"Unknown behavior '{args[1]}'. Valid: seek, arrive, flee, face, facepoint, wander, stop.";
                    return false;
            }
        }

        // Parses a destination token slice: "x z" (two numbers) | "<playerId>" (one int) | "me" (caller).
        private static bool TryParseTargetTokens(string[] tokens, int callerId, out MoveTarget target, out string error)
        {
            target = default;
            error = string.Empty;

            if (tokens.Length == 1)
            {
                string tok = tokens[0];
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

            if (tokens.Length == 2)
            {
                if (!TryFloat(tokens[0], out float x) || !TryFloat(tokens[1], out float z))
                {
                    error = "Destination 'x z' must be two numbers.";
                    return false;
                }
                target = MoveTarget.At(new Vector2(x, z));
                return true;
            }

            error = Usage;
            return false;
        }

        // Index of the "facing" keyword in the token slice, or -1 if absent.
        private static int IndexOfFacing(string[] tokens)
        {
            for (int i = 0; i < tokens.Length; i++)
                if (tokens[i].Equals("facing", System.StringComparison.OrdinalIgnoreCase))
                    return i;
            return -1;
        }

        private static bool TryFloat(string s, out float value) =>
            float.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out value);
    }
}
