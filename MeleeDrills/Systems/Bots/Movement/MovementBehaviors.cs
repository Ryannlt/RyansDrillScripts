using UnityEngine;

namespace MDS.Systems
{
    // Turns steering into a BotIntent. Assemble localizes a world velocity (from the Steering layer,
    // possibly blended) into the input axis + coupled facing. The remaining behaviors here are the ones
    // that don't fit the pure-velocity model: Wander (stateful), Face/FacePoint (rotation only), Stop.
    // No engine I/O - BotController.ApplyIntent issues commands - so these stay unit-testable.
    //
    // A halted result returns a ZERO axis (an explicit stop), never BotIntent.Idle: a null axis means
    // "issue no axis command", leaving the previously-sent axis in place - the bot would keep moving. Run
    // is a sticky mode set elsewhere (once), so it is left null here.
    public static class MovementBehaviors
    {
        private const float EpsilonSqr = 0.0001f;

        // Wander tuning. Offset > radius keeps the wander target always ahead (no spin-in-place).
        public const float DefaultWanderOffset = 2f;    // how far ahead the wander circle projects (m)
        public const float DefaultWanderRadius = 1.2f;  // wander circle radius (m); larger = sharper turns
        public const float DefaultWanderRate = 90f;     // jitter applied to the wander angle (deg/sec)
        public const float DefaultWanderDecay = 1.5f;   // pull of the wander angle back toward straight-ahead (1/sec)
        private const float MaxWanderAngle = 60f;       // hard clamp on the wander angle (deg)

        // Below this net throttle, treat the desired velocity as "at rest" and halt. Our kinematic model has
        // no physical friction, so a tiny residual velocity (e.g. near-balanced Separation forces at an
        // equilibrium) would otherwise be issued forever as perpetual micro-movement. This deadband is that
        // missing friction - it lets blended behaviors settle to a stop. Single behaviors never sit in
        // (0, RestThreshold) (Seek/Flee are 0 or full; Arrive halts at 0.5 throttle), so only blends feel it.
        private const float RestThreshold = 0.15f;

        // Localizes a desired world velocity (from Steering, possibly blended) into a BotIntent: input axis
        // in the bot's frame, facing the direction of travel (coupled). Sub-threshold velocity halts (see
        // RestThreshold). Magnitude beyond 1 is clamped to full throttle.
        public static BotIntent Assemble(BotPose pose, Vector2 worldVelocity)
        {
            float mag = worldVelocity.magnitude;
            if (mag < RestThreshold) return Stop();

            Vector2 dir = worldVelocity / mag;
            float throttle = Mathf.Min(mag, 1f);
            Vector2 axis = MovementSolver.ToLocalAxis(pose, dir, throttle);
            return new BotIntent { MoveAxis = axis, LookHeading = MovementSolver.HeadingOf(dir) };
        }

        // Wander: smooth, undirected roaming (Millington's steering wander), as a WORLD VELOCITY so it can be
        // blended with corrective behaviors (obstacle/collision avoidance). A target rides the rim of a circle
        // projected ahead of the bot; that rim point drifts by a small random amount each tick and the bot
        // Seeks it - producing gentle continuous turns rather than jittery noise. STATEFUL: the caller owns
        // 'wanderAngle' (passed by ref) so it persists across ticks. Uses UnityEngine.Random, so unlike the
        // pure Steering behaviors it is not deterministic. Assemble it (or blend first) to get a BotIntent.
        public static Vector2 WanderVelocity(BotPose pose, ref float wanderAngle, float deltaTime) =>
            WanderVelocity(pose, ref wanderAngle, DefaultWanderOffset, DefaultWanderRadius, DefaultWanderRate, DefaultWanderDecay, deltaTime);

        public static Vector2 WanderVelocity(BotPose pose, ref float wanderAngle, float offset, float radius, float rate, float decay, float deltaTime)
        {
            // Random-walk the wander angle, but pull it back toward 0 (straight ahead) each tick. An
            // unbounded walk parks off-centre and the bot circles forever; this restoring force keeps the
            // angle hovering around forward so the path meanders instead. Clamp is a hard safety cap.
            wanderAngle += RandomBinomial() * rate * deltaTime;
            wanderAngle -= wanderAngle * decay * deltaTime;
            wanderAngle = Mathf.Clamp(wanderAngle, -MaxWanderAngle, MaxWanderAngle);

            Vector2 forward = MovementSolver.DirectionFromHeading(pose.Heading);
            Vector2 circleCenter = pose.Position + forward * offset;
            Vector2 rim = MovementSolver.DirectionFromHeading(pose.Heading + wanderAngle);
            Vector2 target = circleCenter + rim * radius;

            return Steering.Seek(pose, target); // velocity toward the wandering target (offset > radius => always ahead)
        }

        // Triangular random in [-1, 1], biased toward 0 (Millington's randomBinomial).
        private static float RandomBinomial() => Random.value - Random.value;

        // Rotate in place to face a world point (no translation).
        public static BotIntent FacePoint(BotPose pose, Vector2 target)
        {
            if ((target - pose.Position).sqrMagnitude < EpsilonSqr)
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
