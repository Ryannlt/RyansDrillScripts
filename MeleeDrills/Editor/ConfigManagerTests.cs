using NUnit.Framework;
using MDS.ConfigVariables;

[TestFixture]
public class ConfigManagerTests
{
    [SetUp]
    public void SetUp()
    {
        Logger.SetEnableLogging(true);
        Logger.SetEnableDebugLogging(false);
    }

    [Test]
    public void Test_ProcessConfigVariables_SingleValidCommand()
    {
        ConfigManager.ProcessConfigVariables(new string[] { "MDS:EnableDebugLogging:true" });
        Assert.IsTrue(Logger.EnableDebugLogging, "EnableDebugLogging should be true after execution.");
    }

    [Test]
    public void Test_ProcessConfigVariables_MultipleCommands()
    {
        Logger.SetEnableDebugLogging(false);

        ConfigManager.ProcessConfigVariables(new string[]
        {
            "ImpactRating:serverType:NAFriendly",
            "MDS:EnableDebugLogging:true",
            "ImpactRating:serverPassword:1234"
        });

        Assert.IsTrue(Logger.EnableDebugLogging, "EnableDebugLogging should be true after processing MDS command.");
    }

    [Test]
    public void Test_ProcessConfigVariables_OnlyMDSProcessed()
    {
        Logger.SetEnableDebugLogging(false);

        ConfigManager.ProcessConfigVariables(new string[]
        {
            "AnotherMod:EnableDebugLogging:true",
            "MDS:EnableDebugLogging:true",
            "UnrelatedMod:SomeSetting:123"
        });

        Assert.IsTrue(Logger.EnableDebugLogging, "Only MDS commands should be processed.");
    }

    [Test]
    public void Test_ProcessConfigVariables_UnknownCommand()
    {
        Logger.SetEnableDebugLogging(false);

        ConfigManager.ProcessConfigVariables(new string[] { "MDS:UnknownCommand:true" });

        Assert.IsFalse(Logger.EnableDebugLogging, "Unknown config command should not affect debug logging.");
    }

    [Test]
    public void Test_ProcessConfigVariables_InvalidValue_EnableDebugLogging()
    {
        Logger.SetEnableDebugLogging(false);

        ConfigManager.ProcessConfigVariables(new string[] { "MDS:EnableDebugLogging:invalidValue" });

        Assert.IsFalse(Logger.EnableDebugLogging, "Invalid value should not enable debug logging.");
    }

    [Test]
    public void Test_ProcessConfigVariables_MalformedEntry()
    {
        Logger.SetEnableDebugLogging(false);

        // Should not throw or affect config state
        ConfigManager.ProcessConfigVariables(new string[] { "MDS" });

        Assert.IsFalse(Logger.EnableDebugLogging, "Malformed entry should not enable debug logging.");
    }

    [Test]
    public void Test_ProcessConfigVariables_EmptyArray()
    {
        Logger.SetEnableDebugLogging(false);

        ConfigManager.ProcessConfigVariables(new string[] { });

        Assert.IsFalse(Logger.EnableDebugLogging, "Empty array should have no effect.");
    }

    [Test]
    public void Test_ProcessConfigVariables_NullInput()
    {
        Logger.SetEnableDebugLogging(false);

        ConfigManager.ProcessConfigVariables(null);

        Assert.IsFalse(Logger.EnableDebugLogging, "Null input should have no effect.");
    }
}
