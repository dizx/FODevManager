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
        private static UiDispatcher? _uiDispatcher;

        private static UiDispatcher Ui => _uiDispatcher ?? throw new InvalidOperationException("BusyOps.Initialize must be called before using BusyOps.");

        public static void Initialize(UiDispatcher uiDispatcher)
        {
            _uiDispatcher = uiDispatcher ?? throw new ArgumentNullException(nameof(uiDispatcher));
        }

        public readonly record struct TryResult<T>(bool Ok, T Value);

        public static Task<TryResult<T>> TrySyncAsAsync<T>(Func<T> func, string operationName, bool shutdownServer = true, T fallback = default)
            => TryCatchAsync<T>(() => Task.Run(func), operationName, shutdownServer, fallback);

        public static Task<bool> TrySyncAsAsync(Action action, string operationName, bool shutdownServer)
            => TryCatchAsync(() => Task.Run(action), operationName, shutdownServer);

        private static async Task<bool> TryCatchAsync(Func<Task> action, string operationName, bool shutdownServer)
        {
            var result = await TryCatchAsync(
                async () =>
                {
                    await action().ConfigureAwait(true);
                    return true;
                },
                operationName, shutdownServer, fallback: false).ConfigureAwait(true);

            return result.Ok;
        }

        private static async Task<TryResult<T>> TryCatchAsync<T>(Func<Task<T>> func, string operationName, bool shutdownServer, T fallback = default)
        {
            var minVisibleMs = 2000;
            var busy = Singleton<BusyHandler>.Instance;
            var operationId = Guid.NewGuid();
            var stopWatch = Stopwatch.StartNew();

            using (OperationScope.Begin(operationId))
            {
                try
                {
                    await Ui.EnqueueAsync(() =>
                    {
                        busy.Start(operationName, operationId);
                    }).ConfigureAwait(false);

                    // Let UI paint once
                    await Task.Yield();

                    if (shutdownServer)
                    {
                        await Task.Run(ServiceHelper.StopW3SVC).ConfigureAwait(false);
                    }

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

                    await Task.Run(ServiceHelper.StartW3SVC).ConfigureAwait(false);

                    stopWatch.Stop();
                    var remainingMilliseconds = minVisibleMs - (int)stopWatch.ElapsedMilliseconds;
                    if (remainingMilliseconds > 0)
                    {
                        await Task.Delay(remainingMilliseconds).ConfigureAwait(true);
                    }

                    await Ui.EnqueueAsync(() =>
                    {
                        busy.Stop(operationId);
                    }).ConfigureAwait(false);
                }
            }
        }
    }
}
