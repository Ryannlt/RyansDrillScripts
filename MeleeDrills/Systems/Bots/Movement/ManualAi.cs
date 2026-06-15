namespace MDS.Systems
{
    // A manually-driven AI: a test harness for movement behaviors. Holds one current MoveOrder and, each
    // tick, returns the BotIntent for it (via MovementBehaviors). The order is set by 'rc bot move', which
    // reaches ONLY bots already on this AI - it never reassigns AI - so a bot stays under Manual control
    // until an admin explicitly changes its AI. Defaults to Stop (stands still) until ordered.
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
                case MoveOrderKind.Seek:      return MovementBehaviors.Seek(pose, _order.Point);
                case MoveOrderKind.Face:      return MovementBehaviors.Face(_order.Heading);
                case MoveOrderKind.FacePoint: return MovementBehaviors.FacePoint(pose, _order.Point);
                default:                      return MovementBehaviors.Stop();
            }
        }
    }
}
