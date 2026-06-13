namespace MDS.ConsoleCommands
{
    // A 'rc bot <subcommand>' handler. Mirrors IConfigurable: BotCommand parses the subcommand
    // keyword to a BotCommandEnum, looks up the handler, and delegates validation/execution.
    // 'args' are the tokens AFTER the subcommand keyword.
    public interface IBotSubCommand
    {
        BotCommandEnum SubCommandName { get; }

        bool Validate(string[] args, out string errorMessage);
        void Execute(int playerId, string[] args);
    }
}
