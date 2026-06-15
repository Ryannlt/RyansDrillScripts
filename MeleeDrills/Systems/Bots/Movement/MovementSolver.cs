using UnityEngine;

namespace MDS.Systems
{
    // Pure movement math - no engine I/O, so it stays unit-testable. Turns world-space movement goals into
    // the bot's LOCAL input channels using the bot's live heading, because the input axis is always
    // interpreted in the bot's CURRENT facing (strafing left while facing North moves West; turn the body
    // and that same strafe points elsewhere). Callers therefore re-solve every tick against a fresh pose.
    //
    // Vectors are world XZ packed as (x = world X, y = world Z); heading is degrees from North (clockwise).
    public static class MovementSolver
    {
        // Expresses a unit world direction in the bot's local frame, scaled by throttle. The result is the
        // (sideways, forwards) pair SetInputAxis wants: forwards = along the bot's facing, sideways = to its
        // right. Because rotation preserves length, |result| == throttle, so throttle IS the speed fraction
        // (the engine scales it to the active walk/run top speed).
        public static Vector2 ToLocalAxis(BotPose pose, Vector2 worldDir, float throttle)
        {
            throttle = Mathf.Clamp01(throttle);

            float hr = pose.Heading * Mathf.Deg2Rad;
            float sin = Mathf.Sin(hr);
            float cos = Mathf.Cos(hr);

            // forward = (sin, cos), right = (cos, -sin); project worldDir onto each (a -heading rotation).
            float forwards = worldDir.x * sin + worldDir.y * cos;
            float sideways = worldDir.x * cos - worldDir.y * sin;

            return new Vector2(sideways, forwards) * throttle;
        }

        // The heading (degrees from North, in [0, 360)) that points from 'from' toward 'to'.
        public static float HeadingTo(Vector2 from, Vector2 to)
        {
            Vector2 d = to - from;
            float deg = Mathf.Atan2(d.x, d.y) * Mathf.Rad2Deg; // x first: 0 = North (+Z), 90 = East (+X)
            return deg < 0f ? deg + 360f : deg;
        }
    }
}
