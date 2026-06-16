using UnityEngine;

namespace MDS.Systems
{
    // A manually-driven AI: a test harness for movement behaviors. Holds one current MoveOrder and, each
    // tick, returns the BotIntent for it (via MovementBehaviors). The order is set by 'rc bot move', which
    // reaches ONLY bots already on this AI - it never reassigns AI - so a bot stays under Manual control
    // until an admin explicitly changes its AI. Defaults to Stop (stands still) until ordered.
    //
    // A player target is resolved to its LIVE position here every tick, so the pure behaviors keep seeing
    // only a point. If the target player isn't currently spawned, the bot halts until it reappears.
    //
    // Decoupled facing is a thin override: after the translation behavior produces its (coupled) intent,
    // an optional FaceTarget overwrites just the look channel - "run there, but face this". Deliberately
    // minimal; no facing-behavior/compose machinery until we know the shape of blending/arbitration.
    public class ManualAi : IBotAi
    {
        public BotAiEnum AiType => BotAiEnum.Manual;

        private MoveOrder _order = MoveOrder.Stop();
        private float _wanderAngle;   // persistent state for the Wander behavior; reset on each new order

        public void SetOrder(MoveOrder order)
        {
            _order = order;
            _wanderAngle = 0f;
        }

        public BotIntent Decide(BotController self, float deltaTime)
        {
            if (!self.TryGetPose(out BotPose pose))
                return BotIntent.Idle; // not currently spawned - issue nothing

            BotIntent intent;
            switch (_order.Kind)
            {
                case MoveOrderKind.Seek:
                    intent = TryResolve(_order.Target, out Vector2 sp) ? MovementBehaviors.Seek(pose, sp) : MovementBehaviors.Stop();
                    break;
                case MoveOrderKind.Arrive:
                    intent = TryResolve(_order.Target, out Vector2 ap) ? MovementBehaviors.Arrive(pose, ap) : MovementBehaviors.Stop();
                    break;
                case MoveOrderKind.Flee:
                    intent = TryResolve(_order.Target, out Vector2 fp) ? MovementBehaviors.Flee(pose, fp) : MovementBehaviors.Stop();
                    break;
                case MoveOrderKind.FacePoint:
                    intent = TryResolve(_order.Target, out Vector2 pp) ? MovementBehaviors.FacePoint(pose, pp) : MovementBehaviors.Stop();
                    break;
                case MoveOrderKind.Face:
                    intent = MovementBehaviors.Face(_order.Heading);
                    break;
                case MoveOrderKind.Wander:
                    intent = MovementBehaviors.Wander(pose, ref _wanderAngle, deltaTime);
                    break;
                default:
                    intent = MovementBehaviors.Stop();
                    break;
            }

            // Optional decoupled facing: override only the look channel to face a separate target (e.g. face
            // an enemy while seeking/fleeing elsewhere). If the facing target isn't resolvable, the behavior's
            // own facing (face-travel) stands.
            if (_order.FaceTarget.HasValue && TryResolve(_order.FaceTarget.Value, out Vector2 look))
                intent.LookHeading = MovementSolver.HeadingTo(pose.Position, look);

            return intent;
        }

        // Resolves a target to a live world point. A fixed point passes through; a player target is read
        // from its current transform, or fails if that player isn't presently spawned.
        private bool TryResolve(MoveTarget target, out Vector2 point)
        {
            if (!target.IsPlayer)
            {
                point = target.Point;
                return true;
            }

            IPlayer player = StateTracker.GetPlayerById(target.PlayerId);
            if (player?.PlayerObject != null)
            {
                Vector3 wp = player.PlayerObject.transform.position;
                point = new Vector2(wp.x, wp.z);
                return true;
            }

            point = default;
            return false;
        }
    }
}
