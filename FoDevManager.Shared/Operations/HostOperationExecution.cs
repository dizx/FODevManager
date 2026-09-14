using FODevManager.Messages;

namespace FODevManager.Operations;

// Outcome capture is execution-scoped, but never grants lock ownership to nested work
public static class HostOperationExecution
{
    private static readonly AsyncLocal<Capture?> Current = new();
    private static readonly IDisposable Subscription = MessageBus.Subscribe(message =>
    {
        var capture = Current.Value;
        if (capture != null && Volatile.Read(ref capture.Closed) == 0 && message.Type == MessageType.Error)
            Interlocked.Increment(ref capture.Errors);
    });

    public static async Task<T> RunAsync<T>(string name, Func<Task<T>> action, Func<Task> cleanup)
    {
        var previous = Current.Value;
        var capture = new Capture();
        Current.Value = capture;
        try
        {
            T result;
            try { result = await action().ConfigureAwait(false); }
            finally { await cleanup().ConfigureAwait(false); }
            WorkflowContext.Require(Volatile.Read(ref capture.Errors) == 0, name + " reported errors");
            WorkflowContext.Require(result is not null && result is not false, name);
            MessageLogger.Highlight($"✓ {name} completed");
            return result;
        }
        finally
        {
            Interlocked.Exchange(ref capture.Closed, 1);
            Current.Value = previous;
        }
    }

    private sealed class Capture
    {
        public int Errors;
        public int Closed;
    }
}
