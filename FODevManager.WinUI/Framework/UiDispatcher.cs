using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace FODevManager.WinUI.Framework
{
    public sealed class UiDispatcher
    {
        private readonly Microsoft.UI.Dispatching.DispatcherQueue _dispatcherQueue;

        public UiDispatcher(Microsoft.UI.Dispatching.DispatcherQueue dispatcherQueue)
        {
            _dispatcherQueue = dispatcherQueue ?? throw new ArgumentNullException(nameof(dispatcherQueue));
        }

        public Task EnqueueAsync(Action action)
        {
            var taskCompletionSource = new TaskCompletionSource<object?>();

            var enqueued = _dispatcherQueue.TryEnqueue(() =>
            {
                try
                {
                    action();
                    taskCompletionSource.SetResult(null);
                }
                catch (Exception exception)
                {
                    taskCompletionSource.SetException(exception);
                }
            });

            if (!enqueued)
                taskCompletionSource.SetException(new InvalidOperationException("Failed to enqueue work on UI thread."));

            return taskCompletionSource.Task;
        }

        public Task<T> EnqueueAsync<T>(Func<T> func)
        {
            var taskCompletionSource = new TaskCompletionSource<T>();

            var enqueued = _dispatcherQueue.TryEnqueue(() =>
            {
                try
                {
                    taskCompletionSource.SetResult(func());
                }
                catch (Exception exception)
                {
                    taskCompletionSource.SetException(exception);
                }
            });

            if (!enqueued)
                taskCompletionSource.SetException(new InvalidOperationException("Failed to enqueue work on UI thread."));

            return taskCompletionSource.Task;
        }
    }

}
