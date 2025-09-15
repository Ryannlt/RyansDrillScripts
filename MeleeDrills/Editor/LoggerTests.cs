using NUnit.Framework;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.TestTools;
using MDS;

[TestFixture]
public class LoggerTests
{
    private List<string> logMessages;

    [SetUp]
    public void Setup()
    {
        logMessages = new List<string>();
        Application.logMessageReceived += CaptureLog;
    }

    [TearDown]
    public void Teardown()
    {
        Application.logMessageReceived -= CaptureLog;
    }

    private void CaptureLog(string condition, string stackTrace, LogType type)
    {
        Debug.Log($"[TEST DEBUG] CaptureLog Triggered - Type: {type} - Message: {condition}");
        logMessages.Add(condition);
    }

    private string GetLastLog()
    {
        return logMessages.Count > 0 ? logMessages[^1] : "";
    }

    [Test]
    public void Test_Info_Log_Format()
    {
        MDS.Logger.Log("Test Info Message", LogLevel.INFO);

        Assert.IsTrue(logMessages.Count > 0, "Log should have been captured.");
        Assert.That(GetLastLog(), Does.Contain("[MDS]"), "Log should contain the mod name [MDS].");
        Assert.That(GetLastLog(), Does.Contain("[INFO]"), "Log should contain the log level [INFO].");
        Assert.That(GetLastLog(), Does.Contain("Test Info Message"), "Log should contain the expected message.");
    }

    [Test]
    public void Test_Warning_Log_Format()
    {
        MDS.Logger.Log("Test Warning Message", LogLevel.WARNING);

        Assert.IsTrue(logMessages.Count > 0, "Log should have been captured.");
        Assert.That(GetLastLog(), Does.Contain("[MDS]"), "Log should contain the mod name [MDS].");
        Assert.That(GetLastLog(), Does.Contain("[WARNING]"), "Log should contain the log level [WARNING].");
        Assert.That(GetLastLog(), Does.Contain("Test Warning Message"), "Log should contain the expected message.");
    }

    [Test]
    public void Test_Error_Log_Format()
    {
        // Disable Unity's automatic test failure on errors
        LogAssert.ignoreFailingMessages = true;

        MDS.Logger.Log("Test Error Message", LogLevel.ERROR);

        Assert.IsTrue(logMessages.Count > 0, "Log should have been captured.");

        // Create a copy to avoid collection modification errors
        List<string> logSnapshot = new List<string>(logMessages);

        Debug.Log($"[TEST DEBUG] Log count: {logSnapshot.Count}");
        foreach (string log in logSnapshot)
        {
            Debug.Log($"[TEST DEBUG] Captured Log: {log}");
        }

        string errorLog = logSnapshot.Find(log => log.Contains("[ERROR]"));

        Assert.IsNotNull(errorLog, "No ERROR log found in captured logs.");
        Assert.That(errorLog.Contains("[MDS]"), $"Expected log to contain '[MDS]', but got: {errorLog}");
        Assert.That(errorLog.Contains("[ERROR]"), $"Expected log to contain '[ERROR]', but got: {errorLog}");
        Assert.That(errorLog.Contains("Test Error Message"), $"Expected log to contain 'Test Error Message', but got: {errorLog}");

        // Re-enable Unity’s default error handling after the test
        LogAssert.ignoreFailingMessages = false;
    }

    [Test]
    public void Test_Debug_Log_Disabled_By_Default()
    {
        MDS.Logger.Log("Test Debug Message", LogLevel.DEBUG);

        Assert.IsTrue(logMessages.Count == 0, "DEBUG logs should not appear when disabled.");
    }

    [Test]
    public void Test_Debug_Log_Appears_When_Enabled()
    {
        MDS.Logger.SetEnableDebugLogging(true);
        MDS.Logger.Log("Test Debug Message", LogLevel.DEBUG);

        Assert.IsTrue(logMessages.Count > 0, "Log should have been captured.");
        Assert.That(GetLastLog(), Does.Contain("[MDS]"), "Log should contain the mod name [MDS].");
        Assert.That(GetLastLog(), Does.Contain("[DEBUG]"), "Log should contain the log level [DEBUG].");
        Assert.That(GetLastLog(), Does.Contain("Test Debug Message"), "DEBUG log should appear when enabled.");

        MDS.Logger.SetEnableDebugLogging(false); // Reset for other tests
    }

    [Test]
    public void Test_Logging_Can_Be_Disabled()
    {
        MDS.Logger.SetEnableLogging(false);
        MDS.Logger.Log("This should not appear", LogLevel.INFO);

        Assert.IsTrue(logMessages.Count == 0, "No logs should appear when logging is disabled.");

        MDS.Logger.SetEnableLogging(true); // Reset for other tests
    }

    [Test]
    public void Test_Logging_Can_Be_Enabled_Again()
    {
        MDS.Logger.SetEnableLogging(false);
        MDS.Logger.Log("This should not appear", LogLevel.INFO);

        MDS.Logger.SetEnableLogging(true);
        MDS.Logger.Log("Logging enabled again", LogLevel.INFO);
        MDS.Logger.Log("This should appear", LogLevel.INFO);

        Assert.IsTrue(logMessages.Count > 0, "Log should have been captured.");
        Assert.That(logMessages[^2], Does.Contain("Logging enabled again"), "Log should confirm logging was enabled again.");
        Assert.That(GetLastLog(), Does.Contain("This should appear"), "Log should confirm logging is working again.");
    }
}
