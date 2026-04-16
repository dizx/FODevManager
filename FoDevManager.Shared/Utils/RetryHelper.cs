using System;

namespace FODevManager.Shared.Utils
{
    public static class RetryHelper
    {
        public static void RetryOnException(Action operation, int times, int delay = 0)
        {
            RetryOnException<Exception>(operation, times, onRetry: null, delay);
        }

        public static void RetryOnException(Action operation, int times, Action<int, Exception, TimeSpan>? onRetry, int delay = 0)
        {
            RetryOnException<Exception>(operation, times, onRetry, delay);
        }

        public static T RetryOnException<T>(Func<T> operation, int times, int delay = 0)
        {
            return RetryOnException<Exception, T>(operation, times, onRetry: null, delay);
        }

        public static T RetryOnException<T>(Func<T> operation, int times, Action<int, Exception, TimeSpan>? onRetry, int delay = 0)
        {
            return RetryOnException<Exception, T>(operation, times, onRetry, delay);
        }

        public static async Task RetryOnExceptionAsync(Func<Task> operation, int times, int delay = 0)
        {
            await RetryOnExceptionAsync<Exception>(operation, times, delay);
        }

        public static async Task<T> RetryOnExceptionAsync<T>(Func<Task<T>> operation, int times, int delay = 0)
        {
            return await RetryOnExceptionAsync<Exception, T>(operation, times, delay);
        }

        private static void RetryOnException<TException>(Action operation, int times, Action<int, Exception, TimeSpan>? onRetry, int delay = 0)
            where TException : Exception
        {
            if (times <= 0)
                throw new ArgumentOutOfRangeException(nameof(times));

            var attempts = 0;

            while (true)
            {
                try
                {
                    attempts++;
                    operation();
                    return;
                }
                catch (TException exception)
                {
                    if (attempts == times)
                        throw;

                    var retryDelay = GetDelayForAttempt(attempts, delay);
                    onRetry?.Invoke(attempts, exception, retryDelay);
                    Thread.Sleep(retryDelay);
                }
            }
        }

        private static T RetryOnException<TException, T>(Func<T> operation, int times, Action<int, Exception, TimeSpan>? onRetry, int delay = 0)
            where TException : Exception
        {
            if (times <= 0)
                throw new ArgumentOutOfRangeException(nameof(times));

            var attempts = 0;

            while (true)
            {
                try
                {
                    attempts++;
                    return operation();
                }
                catch (TException exception)
                {
                    if (attempts == times)
                        throw;

                    var retryDelay = GetDelayForAttempt(attempts, delay);
                    onRetry?.Invoke(attempts, exception, retryDelay);
                    Thread.Sleep(retryDelay);
                }
            }
        }

        private static async Task RetryOnExceptionAsync<TException>(Func<Task> operation, int times, int delay = 0)
            where TException : Exception
        {
            if (times <= 0)
                throw new ArgumentOutOfRangeException(nameof(times));

            var attempts = 0;

            while (true)
            {
                try
                {
                    attempts++;
                    await operation().ConfigureAwait(false);
                    return;
                }
                catch (TException)
                {
                    if (attempts == times)
                        throw;

                    await Task.Delay(GetDelayForAttempt(attempts, delay)).ConfigureAwait(false);
                }
            }
        }

        private static async Task<T> RetryOnExceptionAsync<TException, T>(Func<Task<T>> operation, int times, int delay = 0)
            where TException : Exception
        {
            if (times <= 0)
                throw new ArgumentOutOfRangeException(nameof(times));

            var attempts = 0;

            while (true)
            {
                try
                {
                    attempts++;
                    return await operation().ConfigureAwait(false);
                }
                catch (TException)
                {
                    if (attempts == times)
                        throw;

                    await Task.Delay(GetDelayForAttempt(attempts, delay)).ConfigureAwait(false);
                }
            }
        }

        private static TimeSpan GetDelayForAttempt(int failedAttempts, int delay)
        {
            if (delay > 0)
                return TimeSpan.FromMilliseconds(delay);

            return TimeSpan.FromSeconds(IncreasingDelayInSeconds(failedAttempts));
        }

        private static readonly int[] DelayPerAttemptInSeconds =
        {
            (int)TimeSpan.FromSeconds(1).TotalSeconds,
            (int)TimeSpan.FromSeconds(2).TotalSeconds,
            (int)TimeSpan.FromSeconds(3).TotalSeconds,
            (int)TimeSpan.FromSeconds(5).TotalSeconds,
            (int)TimeSpan.FromSeconds(10).TotalSeconds
        };

        private static int IncreasingDelayInSeconds(int failedAttempts)
        {
            if (failedAttempts <= 0)
                throw new ArgumentOutOfRangeException(nameof(failedAttempts));

            return failedAttempts > DelayPerAttemptInSeconds.Length
                ? DelayPerAttemptInSeconds.Last()
                : DelayPerAttemptInSeconds[failedAttempts - 1];
        }
    }
}
