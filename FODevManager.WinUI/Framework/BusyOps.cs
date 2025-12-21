using FODevManager.Messages;
using FODevManager.Shared.Utils.FODevManager.WinUI.Services;
using FODevManager.Utils;
using FODevManager.WinUI;
using FODevManager.WinUI.Framework;
using Serilog;
using System;
using System.Diagnostics;
using System.Threading.Tasks;

namespace FODevManager.WinUI.Framework
{
    public static class BusyOps
    {
        public readonly record struct TryResult<T>(bool Ok, T Value);

        public static async Task<TryResult<T>> TryCatchAsync<T>(Func<Task<T>> func, string operationName, T fallback = default)
        {
            var minVisibleMs = 2000;
            var busy = Singleton<BusyHandler>.Instance;
            var operationId = Guid.NewGuid();
            var stopWatch = Stopwatch.StartNew();

            using (OperationScope.Begin(operationId))
            {
                try
                {
                    busy.Start(operationName, operationId);
                    await Task.Yield(); // let UI render overlay

                    ServiceHelper.StopW3SVC();

                    MessageLogger.Highlight($"▶ {operationName} started.");

                    Singleton<W3cServiceState>.Instance.InOperation = true;

                    var result = await func().ConfigureAwait(true);

                    Singleton<W3cServiceState>.Instance.InOperation = false;

                    MessageLogger.Highlight($"✓ {operationName} completed.");
                    return new TryResult<T>(true, result);
                }
                catch (OperationCanceledException cancelException)
                {
                    MessageLogger.Warning($"⏹ {operationName} canceled: {cancelException.Message}");
                    return new TryResult<T>(false, fallback);
                }
                catch (Exception exception)
                {
                    MessageLogger.Error($"✖ {operationName} failed: {exception.Message}");
                    Log.Error(exception.ToString());
                    return new TryResult<T>(false, fallback);
                }
                finally
                {
                    Singleton<W3cServiceState>.Instance.InOperation = false;

                    ServiceHelper.StartW3SVC();

                    stopWatch.Stop();
                    var remainingMilliseconds = minVisibleMs - (int)stopWatch.ElapsedMilliseconds;
                    if (remainingMilliseconds > 0)
                    {
                        await Task.Delay(remainingMilliseconds).ConfigureAwait(true);
                    }

                    busy.Stop(operationId);
                }
            }
        }

        public static Task<TryResult<T>> TrySyncAsAsync<T>(Func<T> func, string operationName, T fallback = default)
            => TryCatchAsync<T>(() => Task.Run(func), operationName, fallback);

        public static async Task<bool> TryCatchAsync(Func<Task> action, string operationName)
        {
            var result = await TryCatchAsync(
                async () =>
                {
                    await action().ConfigureAwait(true);
                    return true;
                },
                operationName,
                fallback: false).ConfigureAwait(true);

            return result.Ok;
        }

        // Overload for sync work
        public static Task<bool> TrySyncAsAsync(Action action, string operationName)
            => TryCatchAsync(() => Task.Run(action), operationName);
    }
}
