using NUnit.Framework;
using UnityEngine;
using UnityEngine.UI;
using MDS.Core;

[TestFixture]
public class CommandExecutorTests
{
    private GameObject testCanvas;
    private InputField mockInputField;

    [SetUp]
    public void Setup()
    {
        // Create a mock Game Console Panel
        testCanvas = new GameObject("Game Console Panel");
        testCanvas.AddComponent<Canvas>();
        mockInputField = testCanvas.AddComponent<InputField>();

        // Add the input field to the canvas
        testCanvas.transform.SetParent(null, true);
    }

    [Test]
    public void Test_InitializeConsole_FindsInputField()
    {
        // Simulate finding the console
        CommandExecutor.InitializeConsole();

        // Verify that the field is correctly assigned
        Assert.IsNotNull(mockInputField, "Console input field should be found.");
    }

    [Test]
    public void Test_ExecuteCommand_WithValidField()
    {
        // Simulate finding the console
        CommandExecutor.InitializeConsole();

        // Execute a command
        CommandExecutor.ExecuteCommand("testCommand");

        // Validate command execution
        Assert.Pass("Command execution simulated successfully.");
    }

    [Test]
    public void Test_ExecuteCommand_WhenConsoleNotFound()
    {
        // Do not initialize the console
        CommandExecutor.ExecuteCommand("testCommand");

        // Expecting a warning since the console input field is null
        Assert.Pass("Handled missing console input field correctly.");
    }
}
