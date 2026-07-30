using UnityEngine;

namespace MDS.Systems
{
    // A brain's desired output for a single tick. A null field means "issue no command on that
    // channel" so brains only emit console commands when something should change (keeps traffic low).
    public struct BotIntent
    {
        public Vector2? MoveAxis;    // (sideways, forwards), each in [-1, 1]
        public float? LookHeading;   // degrees from North
        public bool? Running;        // toggle run

        // The action channel: a single carbonPlayers 'playerAction' token to issue this tick (null = none), such
        // as a melee windup, strike, or block. Actions are edge-triggered, so a brain emits one only on the tick
        // it wants it; held states like a block are started once and stopped later, not re-sent each tick.
        public string Action;

        public static BotIntent Idle => new BotIntent();
    }
}
