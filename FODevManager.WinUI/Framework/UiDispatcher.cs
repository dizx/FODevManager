using Microsoft.UI.Dispatching;
using System;
using System.Threading.Tasks;

namespace FODevManager.WinUI.Framework
{
    public sealed class UiDispatcher
    {
        private readonly DispatcherQueue _dispatcherQueue;

        public UiDispatcher(DispatcherQueue dispatcherQueue)
        {
            _dispatcherQueue = dispatcherQueue ?? throw new ArgumentNullException(nameof(dispatcherQueue));
        }

        public bool HasThreadAccess => _dispatcherQueue.HasThreadAccess;

        public Task EnqueueAsync(Action action)
        {
            if (action == null)
                throw new ArgumentNullException(nameof(action));

            if (_dispatcherQueue.HasThreadAccess)
            {
                action();
                return Task.CompletedTask;
            }

            var taskCompletionSource = new TaskCompletionSource<object?>(TaskCreationOptions.RunContinuationsAsynchronously);

            var enqueued = _dispatcherQueue.TryEnqueue(() =>
            {
                try
                {
                    action();
                    taskCompletionSource.TrySetResult(null);
                }
                catch (Exception exception)
                {
                    taskCompletionSource.TrySetException(exception);
                }
            });

            if (!enqueued)
                taskCompletionSource.TrySetException(new InvalidOperationException("Failed to enqueue work on UI thread."));

            return taskCompletionSource.Task;
        }

        public Task<T> EnqueueAsync<T>(Func<T> func)
        {
            if (func == null)
                throw new ArgumentNullException(nameof(func));

            if (_dispatcherQueue.HasThreadAccess)
            {
                return Task.FromResult(func());
            }

            var taskCompletionSource = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);

            var enqueued = _dispatcherQueue.TryEnqueue(() =>
            {
                try
                {
                    var result = func();
                    taskCompletionSource.TrySetResult(result);
                }
                catch (Exception exception)
                {
                    taskCompletionSource.TrySetException(exception);
                }
            });

            if (!enqueued)
                taskCompletionSource.TrySetException(new InvalidOperationException("Failed to enqueue work on UI thread."));

            return taskCompletionSource.Task;
        }

        public Task EnqueueAsync(Func<Task> func)
        {
            if (func == null)
                throw new ArgumentNullException(nameof(func));

            if (_dispatcherQueue.HasThreadAccess)
            {
                return func();
            }

            var taskCompletionSource = new TaskCompletionSource<object?>(TaskCreationOptions.RunContinuationsAsynchronously);

            var enqueued = _dispatcherQueue.TryEnqueue(async () =>
            {
                try
                {
                    await func().ConfigureAwait(true);
                    taskCompletionSource.TrySetResult(null);
                }
                catch (Exception exception)
                {
                    taskCompletionSource.TrySetException(exception);
                }
            });

            if (!enqueued)
                taskCompletionSource.TrySetException(new InvalidOperationException("Failed to enqueue work on UI thread."));

            return taskCompletionSource.Task;
        }

        public Task<T> EnqueueAsync<T>(Func<Task<T>> func)
        {
            if (func == null)
                throw new ArgumentNullException(nameof(func));

            if (_dispatcherQueue.HasThreadAccess)
            {
                return func();
            }

            var taskCompletionSource = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);

            var enqueued = _dispatcherQueue.TryEnqueue(async () =>
            {
                try
                {
                    var result = await func().ConfigureAwait(true);
                    taskCompletionSource.TrySetResult(result);
                }
                catch (Exception exception)
                {
                    taskCompletionSource.TrySetException(exception);
                }
            });

            if (!enqueued)
                taskCompletionSource.TrySetException(new InvalidOperationException("Failed to enqueue work on UI thread."));

            return taskCompletionSource.Task;
        }
    }
}
