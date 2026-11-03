using FODevManager.Messages;

namespace FODevManager.Operations;

// Acquire only at host boundaries and compose nested service work inside the action
// No ambient ownership: independent async requests can never inherit permission to bypass the lock
public sealed class HostOperationBoundary(ApplicationOperationLock? operationLock = null)
{
    private readonly ApplicationOperationLock operationLock = operationLock ?? new();

    public IDisposable Acquire(string name)
    {
        var lease = operationLock.TryAcquire();
        if (lease != null) return lease;
        var message = $"Cannot start '{name}': FO Dev Manager is busy in another operation. Retry after it completes";
        MessageLogger.Error(message);
        throw new InvalidOperationException(message);
    }

    public T Run<T>(string name, Func<T> action)
    {
        using var lease = Acquire(name);
        return action();
    }

    public async Task<T> RunAsync<T>(string name, Func<Task<T>> action, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        using var lease = Acquire(name);
        cancellationToken.ThrowIfCancellationRequested();
        return await action().ConfigureAwait(false);
    }
}
