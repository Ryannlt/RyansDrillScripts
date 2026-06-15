using UnityEngine;

namespace MDS.Systems
{
    public enum MoveOrderKind { Stop, Seek, Face, FacePoint }

    // A single movement instruction for the Manual test AI. Carries whichever payload its Kind needs
    // (Point for Seek/FacePoint, Heading for Face). Built by the 'rc bot move' command and ticked by
    // ManualAi into a BotIntent via MovementBehaviors.
    public struct MoveOrder
    {
        public MoveOrderKind Kind;
        public Vector2 Point;     // Seek / FacePoint: world XZ target
        public float Heading;     // Face: degrees from North

        public static MoveOrder Stop() => new MoveOrder { Kind = MoveOrderKind.Stop };
        public static MoveOrder Seek(Vector2 point) => new MoveOrder { Kind = MoveOrderKind.Seek, Point = point };
        public static MoveOrder Face(float heading) => new MoveOrder { Kind = MoveOrderKind.Face, Heading = heading };
        public static MoveOrder FacePoint(Vector2 point) => new MoveOrder { Kind = MoveOrderKind.FacePoint, Point = point };

        // Whether carrying out this order involves translation (so the actuator should enable running).
        public bool IsTranslating => Kind == MoveOrderKind.Seek;
    }
}
