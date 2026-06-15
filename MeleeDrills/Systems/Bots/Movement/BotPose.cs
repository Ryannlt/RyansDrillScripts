using UnityEngine;

namespace MDS.Systems
{
    // A planar snapshot of a bot's pose, taken fresh each tick from its transform. This is Millington's
    // "Static" data (position + orientation). We omit velocity and rotation (angular velocity) because the
    // engine owns inertia and turn rate - the AI commands a desired velocity directly rather than building
    // it up through accelerations.
    //
    // Convention (shared across the mod): a planar world point/direction is packed into a Vector2 as
    // (x = world X, y = world Z). Heading is degrees from North (= transform.eulerAngles.y), measured
    // clockwise, so 0 = +Z (North) and 90 = +X (East). Heading is the frame SetInputAxis is interpreted
    // in, so it must be read live each tick.
    public struct BotPose
    {
        public Vector2 Position;   // world XZ
        public float Heading;      // degrees from North

        public BotPose(Vector2 position, float heading)
        {
            Position = position;
            Heading = heading;
        }
    }
}
