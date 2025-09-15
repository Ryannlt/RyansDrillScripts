using NUnit.Framework;
using MDS.Events;

[TestFixture]
public class EventDispatcherTests
{
    private class MockEvent : IEvent
    {
        public EventEnum EventName => EventEnum.TestEvent;
        public bool Triggered { get; private set; }
        public object[] ReceivedParams { get; private set; }

        public bool Validate(object[] parameters, out string errorMessage)
        {
            errorMessage = string.Empty;
            if (parameters != null && parameters.Length == 2)
            {
                return true;
            }

            errorMessage = "Expected exactly 2 parameters.";
            return false;
        }

        public void Trigger(object[] parameters)
        {
            Triggered = true;
            ReceivedParams = parameters;
        }
    }

    private class InvalidMockEvent : IEvent
    {
        public EventEnum EventName => EventEnum.TestEvent;

        public bool Validate(object[] parameters, out string errorMessage)
        {
            errorMessage = "Invalid for test.";
            return false;
        }

        public void Trigger(object[] parameters)
        {
            Assert.Fail("Trigger should not be called when validation fails.");
        }
    }

    [SetUp]
    public void Setup()
    {
        EventDispatcher.Register(EventEnum.TestEvent, new MockEvent());
    }

    [Test]
    public void Test_Predefined_Event_Is_Registered()
    {
        bool success = EventDispatcher.Trigger(EventEnum.TestEvent, new object[] { "a", "b" }, out string error);
        Assert.IsTrue(success, "Triggering registered event with valid parameters should succeed.");
        Assert.IsEmpty(error);
    }

    [Test]
    public void Test_Trigger_Custom_Mock_Event()
    {
        var mock = new MockEvent();
        EventDispatcher.Register(EventEnum.TestEvent, mock);

        bool success = EventDispatcher.Trigger(EventEnum.TestEvent, new object[] { "param1", "param2" }, out string error);

        Assert.IsTrue(success, "Event should have been triggered successfully.");
        Assert.IsEmpty(error);
        Assert.IsTrue(mock.Triggered, "Event logic should have run.");
        Assert.IsNotNull(mock.ReceivedParams);
        Assert.AreEqual(2, mock.ReceivedParams.Length);
        Assert.AreEqual("param1", mock.ReceivedParams[0]);
        Assert.AreEqual("param2", mock.ReceivedParams[1]);
    }

    [Test]
    public void Test_Trigger_Unregistered_Event_Does_Not_Throw()
    {
        bool success = EventDispatcher.Trigger((EventEnum)999, new object[] { "param" }, out string error);

        Assert.IsFalse(success);
        Assert.IsNotEmpty(error);
        Assert.That(error, Does.Contain("unregistered event"));
    }

    [Test]
    public void Test_Trigger_Fails_When_Validation_Fails()
    {
        EventDispatcher.Register(EventEnum.TestEvent, new InvalidMockEvent());

        bool success = EventDispatcher.Trigger(EventEnum.TestEvent, new object[] { "only_one_param" }, out string error);

        Assert.IsFalse(success, "Validation should fail with incorrect parameters.");
        Assert.AreEqual("Invalid for test.", error);
    }
}
