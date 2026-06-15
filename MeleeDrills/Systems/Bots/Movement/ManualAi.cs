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
    public class ManualAi : IBotAi
    {
        public BotAiEnum AiType => BotAiEnum.Manual;

        private MoveOrder _order = MoveOrder.Stop();

        public void SetOrder(MoveOrder order) => _order = order;

        public BotIntent Decide(BotController self, float deltaTime)
        {
            if (!self.TryGetPose(out BotPose pose))
                return BotIntent.Idle; // not currently spawned - issue nothing

            switch (_order.Kind)
            {
                case MoveOrderKind.Seek:
                    return TryResolveTarget(out Vector2 sp) ? MovementBehaviors.Seek(pose, sp) : MovementBehaviors.Stop();
                case MoveOrderKind.Arrive:
                    return TryResolveTarget(out Vector2 ap) ? MovementBehaviors.Arrive(pose, ap) : MovementBehaviors.Stop();
                case MoveOrderKind.Flee:
                    return TryResolveTarget(out Vector2 fp) ? MovementBehaviors.Flee(pose, fp) : MovementBehaviors.Stop();
                case MoveOrderKind.FacePoint:
                    return TryResolveTarget(out Vector2 pp) ? MovementBehaviors.FacePoint(pose, pp) : MovementBehaviors.Stop();
                case MoveOrderKind.Face:
                    return MovementBehaviors.Face(_order.Heading);
                default:
                    return MovementBehaviors.Stop();
            }
        }

        // Resolves the order's target to a live world point. A fixed point passes through; a player target
        // is read from its current transform, or fails if that player isn't presently spawned.
        private bool TryResolveTarget(out Vector2 point)
        {
            MoveTarget target = _order.Target;
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
