using FODevManager.Messages;
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
            var minVisibleMs = 750;
            var busy = Singleton<BusyHandler>.Instance;
            var opId = Guid.NewGuid();
            var stopWatch = Stopwatch.StartNew();

            using (OperationScope.Begin(opId))
            {
                try
                {
                    busy.Start(operationName, opId);
                    await Task.Yield(); // let UI render overlay
                    MessageLogger.Highlight($"▶ {operationName} started.");

                    var result = await func().ConfigureAwait(true);

                    MessageLogger.Highlight($"✓ {operationName} completed.");
                    return new(true, result);
                }
                catch (OperationCanceledException cancelException)
                {
                    // Optionally treat cancel differently from "hard" failures
                    MessageLogger.Warning($"⏹ {operationName} canceled: {cancelException.Message}");
                    
                    return new(false, fallback);
                }
                catch (Exception ex)
                {
                    MessageLogger.Error($"✖ {operationName} failed: {ex.Message}");
                    Log.Error(ex.ToString());
                    return new(false, fallback);
                }
                finally
                {
                    stopWatch.Stop();
                    var remaining = minVisibleMs - (int)stopWatch.ElapsedMilliseconds;
                    if (remaining > 0)
                    {
                        await Task.Delay(remaining).ConfigureAwait(true); // keep UI ctx
                    }
                    busy.Stop(opId);
                }
            }
        }

        public static Task<TryResult<T>> TrySyncAsAsync<T>(Func<T> func, string operationName, T fallback = default)
            => TryCatchAsync<T>(() => Task.Run(func), operationName, fallback);

        public static async Task<bool> TryCatchAsync(Func<Task> action, string operationName)
        {
            var minVisibleMs = 750;
            var busy = Singleton<BusyHandler>.Instance;
            var opId = Guid.NewGuid();
            var stopWatch = Stopwatch.StartNew();

            using (OperationScope.Begin(opId))
            {
                try
                {
                    busy.Start(operationName, opId);
                    await Task.Yield(); // let UI render overlay
                    MessageLogger.Highlight($"▶ {operationName} started.");

                    await action();

                    MessageLogger.Highlight($"✓ {operationName} completed.");
                    return true;
                }
                catch (Exception ex)
                {
                    MessageLogger.Error($"✖ {operationName} failed: {ex.Message}");
                    Log.Error(ex.ToString());
                    return false;
                }
                finally
                {
                    stopWatch.Stop();

                    var remaining = minVisibleMs - (int)stopWatch.ElapsedMilliseconds;
                    if (remaining > 0)
                    {
                        await Task.Delay(remaining).ConfigureAwait(true); // stay on UI context after await
                    }

                    busy.Stop(opId);
                }
            }
        }

        // Overload for sync work
        public static Task<bool> TrySyncAsAsync(Action action, string operationName)
            => TryCatchAsync(() => Task.Run(action), operationName);
    }
}
