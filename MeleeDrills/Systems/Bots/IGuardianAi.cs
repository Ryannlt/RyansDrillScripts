namespace MDS.Systems
{
    // An IBotAi that escorts a friendly player rather than picking its own fights. The summon commands hand the
    // bot the player it was summoned onto, so 'rc bot summonAt <id> ... Guardian' produces a bodyguard for that
    // player with no further setup. The same thing can be set later on any group with
    // 'rc bot cfg <target> guardTarget <playerId>'. See MeleeAi for the implementation.
    public interface IGuardianAi
    {
        void SetGuardTarget(int playerId);
    }
}
