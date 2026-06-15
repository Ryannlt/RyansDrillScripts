using UnityEngine;

namespace MDS.Systems
{
    // Pure movement behaviors: given the bot's live pose and a goal, return the BotIntent that realizes it
    // this tick. No engine I/O - BotController.ApplyIntent issues the actual commands - so these stay
    // unit-testable. Each behavior is a small function over MovementSolver; add Arrive/Flee/Pursue/etc here
    // the same way, and only promote to behavior OBJECTS once one needs persistent state or weighted blending.
    //
    // Important: an "arrived" or halted result returns a ZERO axis (an explicit stop), never BotIntent.Idle.
    // A null axis means "issue no axis command", which leaves the previously-sent axis in place - the bot
    // would keep moving. Run is a sticky mode set elsewhere (once), so it is left null here.
    public static class MovementBehaviors
    {
        // Within this squared planar distance, treat the bot as "at" the target (avoids a zero-length
        // direction and post-arrival jitter).
        private const float ArriveEpsilonSqr = 0.0001f;

        // Seek: move toward a world point at full throttle, facing the direction of travel (coupled). Halts
        // once arrived. Faithful to Millington's kinematic seek (velocity straight at the target).
        public static BotIntent Seek(BotPose pose, Vector2 target)
        {
            Vector2 toTarget = target - pose.Position;
            if (toTarget.sqrMagnitude < ArriveEpsilonSqr)
                return Stop();

            Vector2 axis = MovementSolver.ToLocalAxis(pose, toTarget.normalized, 1f);
            float heading = MovementSolver.HeadingTo(pose.Position, target);
            return new BotIntent { MoveAxis = axis, LookHeading = heading };
        }

        // Rotate in place to face a world point (no translation).
        public static BotIntent FacePoint(BotPose pose, Vector2 target)
        {
            if ((target - pose.Position).sqrMagnitude < ArriveEpsilonSqr)
                return Stop(); // on top of the point: nothing meaningful to face, just halt
            return new BotIntent { MoveAxis = Vector2.zero, LookHeading = MovementSolver.HeadingTo(pose.Position, target) };
        }

        // Rotate in place to an absolute heading (degrees from North), no translation.
        public static BotIntent Face(float heading)
        {
            return new BotIntent { MoveAxis = Vector2.zero, LookHeading = heading };
        }

        // Halt translation (explicit zero axis; leaves facing as-is).
        public static BotIntent Stop()
        {
            return new BotIntent { MoveAxis = Vector2.zero };
        }
    }
}
