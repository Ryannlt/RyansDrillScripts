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

        // Default Arrive radii: start slowing within SlowRadius, fully halt within ArriveRadius (metres).
        public const float DefaultSlowRadius = 3f;
        public const float DefaultArriveRadius = 0.3f;

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

        // Arrive: like Seek, but ramps throttle down inside slowRadius and halts inside arriveRadius, for a
        // smooth stop instead of Seek's hard stop. Because the input axis scales speed, the throttle ramp
        // alone produces the deceleration - no real speed constants needed (Millington's Arrive, normalized).
        public static BotIntent Arrive(BotPose pose, Vector2 target) =>
            Arrive(pose, target, DefaultSlowRadius, DefaultArriveRadius);

        public static BotIntent Arrive(BotPose pose, Vector2 target, float slowRadius, float arriveRadius)
        {
            Vector2 toTarget = target - pose.Position;
            float dist = toTarget.magnitude;
            if (dist < arriveRadius)
                return Stop();

            float throttle = dist >= slowRadius ? 1f : dist / slowRadius;
            Vector2 dir = toTarget / dist; // normalized; dist > arriveRadius > 0
            Vector2 axis = MovementSolver.ToLocalAxis(pose, dir, throttle);
            return new BotIntent { MoveAxis = axis, LookHeading = MovementSolver.HeadingOf(dir) };
        }

        // Flee: move directly away from a world point at full throttle, facing the direction of travel
        // (i.e. facing away - the coupled case). A "backpedal" variant that flees while still facing the
        // threat is a future DECOUPLED behavior. Mirror of Seek.
        public static BotIntent Flee(BotPose pose, Vector2 threat)
        {
            Vector2 away = pose.Position - threat;
            if (away.sqrMagnitude < ArriveEpsilonSqr)
                return Stop(); // exactly overlapping: no defined away-direction

            Vector2 dir = away.normalized;
            Vector2 axis = MovementSolver.ToLocalAxis(pose, dir, 1f);
            return new BotIntent { MoveAxis = axis, LookHeading = MovementSolver.HeadingOf(dir) };
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
