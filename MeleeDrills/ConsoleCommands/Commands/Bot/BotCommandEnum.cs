namespace MDS.ConsoleCommands
{
    // Subcommands of 'rc bot'. Parsed by BotCommand, handled by IBotSubCommand implementations.
    public enum BotCommandEnum
    {
        Spawn,
        SpawnRandom,
        Summon,
        Ai,
        Death,
        Remove,
        List
        // Add more bot subcommands here as needed
    }
}
