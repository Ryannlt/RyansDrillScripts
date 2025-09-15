using NUnit.Framework;
using MDS;
using MDS.ConfigVariables;

[TestFixture]
public class ConfigVariableTests
{
    private EnableDebugLogging enableDebugLogging;

    [SetUp]
    public void SetUp()
    {
        enableDebugLogging = new EnableDebugLogging();
    }

    [Test]
    public void Test_EnableDebugLogging_ValidTrue()
    {
        Assert.IsTrue(enableDebugLogging.Validate("true"), "Validate should return true for 'true'.");
    }

    [Test]
    public void Test_EnableDebugLogging_ValidFalse()
    {
        Assert.IsTrue(enableDebugLogging.Validate("false"), "Validate should return true for 'false'.");
    }

    [Test]
    public void Test_EnableDebugLogging_InvalidValue()
    {
        Assert.IsFalse(enableDebugLogging.Validate("invalidValue"), "Validate should return false for an invalid value.");
    }

    [Test]
    public void Test_EnableDebugLogging_Execution()
    {
        enableDebugLogging.Execute("true");
        Assert.IsTrue(Logger.EnableDebugLogging, "EnableDebugLogging should be true after execution.");

        enableDebugLogging.Execute("false");
        Assert.IsFalse(Logger.EnableDebugLogging, "EnableDebugLogging should be false after execution.");
    }
}
