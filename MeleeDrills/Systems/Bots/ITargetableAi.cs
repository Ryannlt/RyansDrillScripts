namespace MDS.Systems
{
    // An IBotAi whose target can be set from the OUTSIDE (a command/event/supervisor), rather than only
    // auto-acquired inside Decide(). This is the seam for target control and sparring: micro-behaviour (block,
    // riposte, spacing) stays inside the AI, while WHO to fight - pin to a player, retarget on death - is a
    // higher-layer decision. See MeleeAi for the first implementation; a supervisor that drives it comes later.
    public interface ITargetableAi
    {
        // Pin a preferred target by player id, or null to clear the pin and auto-acquire the nearest enemy.
        void SetTarget(int? playerId);
    }
}
