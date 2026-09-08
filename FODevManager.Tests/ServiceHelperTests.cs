using FODevManager.Utils;
using FODevManager.Shared.Utils.FODevManager.WinUI.Services;
using FODevManager.Shared.Utils;
using FODevManager.Messages;

namespace FODevManager.Tests;

[TestFixture]
[NonParallelizable]
public class ServiceHelperTests
{
    [TestCase(false, EnvironmentType.Console, 0)]
    [TestCase(true, EnvironmentType.Console, 0)]
    [TestCase(false, EnvironmentType.Console, 5)]
    [TestCase(true, EnvironmentType.Console, 5)]
    [TestCase(false, EnvironmentType.WinUi, 5)]
    [TestCase(true, EnvironmentType.WinUi, 5)]
    public void ProcessExitControlsCacheAndConsoleStopFailureAbortsCaller(bool running, EnvironmentType environment, int exitCode)
    {
        var state = Singleton<W3cServiceState>.Instance;
        var engine = Singleton<Engine>.Instance;
        var previousRunning = state.IsRunning;
        var previousOperation = state.InOperation;
        var previousEnvironment = engine.EnvironmentType;
        var messages = new List<Message>();
        using var subscription = MessageBus.Subscribe(messages.Add);
        try
        {
            state.InOperation = false;
            state.IsRunning = !running;
            engine.EnvironmentType = environment;
            var continued = false;
            void Invoke()
            {
                ServiceHelper.ChangeW3SVCState(running, action =>
                {
                    Assert.That(action, Is.EqualTo(running ? "start" : "stop"));
                    return (exitCode, "standard output", "standard error");
                });
                continued = true;
            }

            var shouldThrow = exitCode != 0 && !running && environment == EnvironmentType.Console;
            if (shouldThrow)
                Assert.Throws<InvalidOperationException>(Invoke);
            else
                Assert.DoesNotThrow(Invoke);

            Assert.Multiple(() =>
            {
                Assert.That(continued, Is.EqualTo(!shouldThrow));
                Assert.That(state.IsRunning, Is.EqualTo(exitCode == 0 ? running : !running));
                var errors = messages.Where(message => message.Type == MessageType.Error).ToList();
                Assert.That(errors, Has.Count.EqualTo(exitCode == 0 ? 0 : 1));
                if (exitCode != 0)
                {
                    Assert.That(errors[0].Content, Does.Contain("code 5").And.Contain("standard output").And.Contain("standard error"));
                    Assert.That(messages.Any(message => message.Content is "W3SVC started" or "W3SVC stopped"), Is.False);
                }
            });
        }
        finally
        {
            state.IsRunning = previousRunning;
            state.InOperation = previousOperation;
            engine.EnvironmentType = previousEnvironment;
        }
    }

    [Test]
    public void ConsoleStopLaunchFailureThrowsAndPreservesCache()
    {
        var state = Singleton<W3cServiceState>.Instance;
        var engine = Singleton<Engine>.Instance;
        var previousRunning = state.IsRunning;
        var previousOperation = state.InOperation;
        var previousEnvironment = engine.EnvironmentType;
        try
        {
            state.InOperation = false;
            state.IsRunning = true;
            engine.EnvironmentType = EnvironmentType.Console;
            Assert.Throws<InvalidOperationException>(() => ServiceHelper.ChangeW3SVCState(false,
                _ => throw new InvalidOperationException("Process launch failed")));
            Assert.That(state.IsRunning, Is.True);
        }
        finally
        {
            state.IsRunning = previousRunning;
            state.InOperation = previousOperation;
            engine.EnvironmentType = previousEnvironment;
        }
    }

    [Test]
    public void FailedStateQueryPropagatesWithoutChangingCache()
    {
        var state = Singleton<W3cServiceState>.Instance;
        var previous = state.IsRunning;
        Assert.Throws<InvalidOperationException>(() => ServiceHelper.RefreshW3SVCState(
            () => throw new InvalidOperationException("Service unavailable")));
        Assert.That(state.IsRunning, Is.EqualTo(previous));
    }

    [TestCase(true)]
    [TestCase(false)]
    public void RefreshUsesActualStateRatherThanCachedValue(bool running)
    {
        var state = Singleton<W3cServiceState>.Instance;
        var previous = state.IsRunning;
        try
        {
            state.IsRunning = !running;
            ServiceHelper.RefreshW3SVCState(() => running);
            Assert.That(state.IsRunning, Is.EqualTo(running));
        }
        finally
        {
            state.IsRunning = previous;
        }
    }
}
