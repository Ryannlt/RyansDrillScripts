namespace MDS.Systems
{
    // An IBotAi whose target can be set from outside (a command, event, or supervisor) rather than only being
    // auto-acquired inside Decide. This is the seam for target control: the AI keeps its own micro-behaviour
    // (block, riposte, spacing), while who to fight (pin to a player, retarget on death) can be a higher-layer
    // decision. See MeleeAi for the first implementation; a supervisor that drives it comes later.
    public interface ITargetableAi
    {
        // Pin a preferred target by player id, or null to clear the pin and auto-acquire the nearest enemy.
        void SetTarget(int? playerId);

        // Whoever the AI settled on last tick, or null if it has no target. This is a report, not a provocation:
        // a bot waiting to be attacked reports the nearest player simply because it is watching them. Forming or
        // waking a formation from this makes passive bots gang up on a passer-by and walk themselves into a fight
        // nobody started, so both come from ISquadMember.ProvokedBy instead. Nothing in the squad layer reads
        // this any more, and it should stay that way.
        int? CurrentTargetId { get; }
    }
}
