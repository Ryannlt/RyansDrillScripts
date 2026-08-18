namespace MDS.Systems
{
    // What a group is doing right now. Held per group by SquadCoordinator and handed to each member on its slot.
    // Breaking is skipped entirely when the Breakoff setting is off, so a station can be told to fight from where
    // the provoking blow left it instead of resetting the distance first.
    public enum SquadPhase
    {
        Posted,      // waiting at the post, nobody has provoked it
        Breaking,    // provoked: re-establishing distance and formation, not yet swinging
        Engaged,     // fighting
        Withdrawing  // breaking off from a live enemy: guard up and still countering, but giving ground and not pressing
    }

    // The spacing a squad is laid out with. Read from the AI's levers so a formation can be tuned live with
    // 'rc bot cfg' like everything else.
    public struct SquadSettings
    {
        public float Spacing;        // gap between neighbouring members, the diameter of a pair's circle
        public float LaneHalfWidth;  // how close a squadmate may be to the swing line before it counts as blocked
        public float Standoff;       // range the formation's point holds from the enemy
        public bool Post;            // wait at the post until provoked, and return to it afterwards
        public bool Breakoff;        // once provoked, re-establish range before swinging (needs Post)
        public float BreakoffRange;  // furthest the group gives ground when breaking off, measured from where it was provoked
        public float EngageDelay;    // seconds after the first provocation before the group may swing
        public float ResetRange;     // how far the target may get from the post before disengaging (0 = no limit)
        public int MinMembers;       // fewest members it will fight with; below this it breaks off and stays shut
        public float ReturnDelay;    // seconds it lingers where the bout ended before walking back to the post

        // Set with an object initializer rather than a constructor: at this many fields a positional call is a
        // row of bare numbers and booleans that is easy to mis-order and impossible to read at the call site.
    }

    // An IBotAi willing to fight as part of a squad. SquadCoordinator groups these by the batch they spawned in
    // and hands each one a slot to hold; the AI stays responsible for the fighting itself.
    public interface ISquadMember
    {
        bool WantsSquad { get; }
        SquadSettings SquadSettings { get; }

        // Who actually provoked this bot, or null while it is only watching someone. Deliberately not the current
        // target: a waiting bot faces and blocks whoever is nearest, so targeting alone would wake a whole station
        // at a passer-by. Only a real provocation counts.
        int? ProvokedBy { get; }

        // Wake this bot onto a target as though it had been provoked itself. This is how one member being stabbed
        // pulls the rest of its group into the fight.
        void Engage(int playerId);

        // Drop the fight and go back to waiting, once the target is dead or has left the station behind.
        void StandDown();
    }
}
