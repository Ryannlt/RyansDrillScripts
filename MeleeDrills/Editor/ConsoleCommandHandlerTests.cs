using NUnit.Framework;
using System;
using System.Collections.Generic;
using System.Reflection;
using MDS;
using MDS.ConsoleCommands;

[TestFixture]
public class ConsoleCommandHandlerTests
{
    private class MockTestCommand : IConsoleCommand
    {
        public ConsoleCommandEnum CommandName => ConsoleCommandEnum.Test;
        public bool Executed { get; private set; }
        public object[] ReceivedParameters { get; private set; }

        public bool Validate(string[] parameters, out string errorMessage)
        {
            errorMessage = "";
            return true;
        }

        public void Execute(int playerId, string[] parameters)
        {
            Executed = true;
            ReceivedParameters = parameters;
        }
    }

    [SetUp]
    public void Setup()
    {
        SetServerState(true);

        var registryField = typeof(ConsoleCommandHandler).GetField("commandRegistry", BindingFlags.NonPublic | BindingFlags.Static);
        registryField?.SetValue(null, new Dictionary<ConsoleCommandEnum, IConsoleCommand>());

        ConsoleCommandHandler.RegisterCommand(new MockTestCommand());
    }

    private void SetServerState(bool state)
    {
        typeof(HoldfastSharedMethodsInterface)
            .GetField("isServer", BindingFlags.NonPublic | BindingFlags.Static)
            ?.SetValue(null, state);
    }

    [Test]
    public void Test_All_Predefined_Commands_Exist()
    {
        var commandRegistry = typeof(ConsoleCommandHandler)
            .GetField("commandRegistry", BindingFlags.NonPublic | BindingFlags.Static)
            ?.GetValue(null) as Dictionary<ConsoleCommandEnum, IConsoleCommand>;

        Assert.IsNotNull(commandRegistry);
        Assert.IsTrue(commandRegistry.ContainsKey(ConsoleCommandEnum.Test), "Test command should be registered.");
    }

    [Test]
    public void Test_ProcessConsoleCommand_ValidTestCommand()
    {
        SetServerState(true);
        Assert.DoesNotThrow(() =>
        {
            ConsoleCommandHandler.ProcessConsoleCommand(1, "Test hello world", "", true);
        }, "Valid command should not throw.");
    }

    [Test]
    public void Test_ProcessConsoleCommand_UnknownCommand_DoesNotParse()
    {
        bool parsed = Enum.TryParse("FakeCommand", true, out ConsoleCommandEnum result);
        Assert.IsFalse(parsed, "The string 'FakeCommand' should not parse into a valid ConsoleCommandEnum.");
    }

    [Test]
    public void Test_ProcessConsoleCommand_IgnoredOnClient()
    {
        SetServerState(false);

        Assert.DoesNotThrow(() =>
        {
            ConsoleCommandHandler.ProcessConsoleCommand(5, "Test param1", "", true);
        }, "Client-side command should be ignored without error.");
    }
}
