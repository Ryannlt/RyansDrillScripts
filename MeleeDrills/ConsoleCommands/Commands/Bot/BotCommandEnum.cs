namespace MDS.ConsoleCommands
{
    // Subcommands of 'rc bot'. Parsed by BotCommand, handled by IBotSubCommand implementations.
    public enum BotCommandEnum
    {
        Spawn,
        SpawnRandom,
        Summon,
        SetBotAi,
        SetBotDeathPolicy,
        Remove,
        List
        // Add more bot subcommands here as needed
    }
}
