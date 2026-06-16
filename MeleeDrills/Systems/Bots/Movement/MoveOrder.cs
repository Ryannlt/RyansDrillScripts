using UnityEngine;

namespace MDS.Systems
{
    public enum MoveOrderKind { Stop, Seek, Arrive, Flee, Face, FacePoint, Wander }

    // A destination for a point-based movement order: either a fixed world point, or a player resolved to
    // its LIVE position each tick (so a bot can chase / flee / track a moving player). Resolving a player
    // to a point is a harness/decision concern - the pure MovementBehaviors only ever see the Vector2.
    public struct MoveTarget
    {
        public bool IsPlayer;
        public int PlayerId;      // when IsPlayer
        public Vector2 Point;     // when !IsPlayer

        public static MoveTarget At(Vector2 point) => new MoveTarget { Point = point };
        public static MoveTarget Player(int playerId) => new MoveTarget { IsPlayer = true, PlayerId = playerId };
    }

    // A single movement instruction for the Manual test AI. Carries whichever payload its Kind needs
    // (Target for Seek/Arrive/Flee/FacePoint, Heading for Face). Built by 'rc bot move' and ticked by
    // ManualAi into a BotIntent via MovementBehaviors.
    public struct MoveOrder
    {
        public MoveOrderKind Kind;
        public MoveTarget Target;        // Seek / Arrive / Flee / FacePoint
        public float Heading;            // Face: degrees from North
        public MoveTarget? FaceTarget;   // optional decoupled facing for Seek/Arrive/Flee (null => face travel)

        public static MoveOrder Stop() => new MoveOrder { Kind = MoveOrderKind.Stop };
        public static MoveOrder Seek(MoveTarget target) => new MoveOrder { Kind = MoveOrderKind.Seek, Target = target };
        public static MoveOrder Arrive(MoveTarget target) => new MoveOrder { Kind = MoveOrderKind.Arrive, Target = target };
        public static MoveOrder Flee(MoveTarget target) => new MoveOrder { Kind = MoveOrderKind.Flee, Target = target };
        public static MoveOrder FacePoint(MoveTarget target) => new MoveOrder { Kind = MoveOrderKind.FacePoint, Target = target };
        public static MoveOrder Face(float heading) => new MoveOrder { Kind = MoveOrderKind.Face, Heading = heading };
        public static MoveOrder Wander() => new MoveOrder { Kind = MoveOrderKind.Wander };

        // Whether carrying out this order involves translation (so the actuator should enable running).
        public bool IsTranslating =>
            Kind == MoveOrderKind.Seek || Kind == MoveOrderKind.Arrive ||
            Kind == MoveOrderKind.Flee || Kind == MoveOrderKind.Wander;
    }
}
