using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace FODevManager.Shared.Utils
{
    public static class AsyncHelpers
    {
        public static async Task ForEachAsync<T>(this IEnumerable<T> items, Func<T, Task> action, int maxDegreeOfParallelism, CancellationToken token)
        {
            var throttler = new SemaphoreSlim(maxDegreeOfParallelism);

            async Task InvokeThrottled(T item)
            {
                await throttler.WaitAsync(token);
                try
                {
                    await action(item);
                }
                finally
                {
                    throttler.Release();
                }
            }

            using (throttler)
            {
                await Task.WhenAll(items.Select(InvokeThrottled));
            }
        }

        public static async Task ForEachAsync<T>(this ConcurrentBag<T> items, Func<T, Task> action, int maxDegreeOfParallelism, CancellationToken token)
        {
            var throttler = new SemaphoreSlim(maxDegreeOfParallelism);

            async Task InvokeThrottled(T item)
            {
                await throttler.WaitAsync(token);
                try
                {
                    await action(item);
                }
                finally
                {
                    throttler.Release();
                }
            }

            using (throttler)   
            {
                await Task.WhenAll(items.Select(InvokeThrottled));
            }
        }

        private static readonly TaskFactory _taskFactory = new
            TaskFactory(CancellationToken.None,
                TaskCreationOptions.None,
                TaskContinuationOptions.None,
                TaskScheduler.Default);

        public static TResult RunSync<TResult>(Func<Task<TResult>> func)
            => _taskFactory
                .StartNew(func)
                .Unwrap()
                .GetAwaiter()
                .GetResult();

        public static void RunSync(Func<Task> func)
            => _taskFactory
                .StartNew(func)
                .Unwrap()
                .GetAwaiter()
                .GetResult();
    }
}
