using FODevManager.Messages;
using FODevManager.Operations;
using FODevManager.Shared.Utils.FODevManager.WinUI.Services;
using FODevManager.Utils;
using FODevManager.WinUI;
using FODevManager.WinUI.Framework;
using System;
using System.Diagnostics;
using System.Threading.Tasks;

namespace FODevManager.WinUI.Framework
{
    public static class BusyOps
    {
        private static UiDispatcher? _uiDispatcher;

        private static UiDispatcher Ui => _uiDispatcher ?? throw new InvalidOperationException("BusyOps.Initialize must be called before using BusyOps");

        public static void Initialize(UiDispatcher uiDispatcher)
        {
            _uiDispatcher = uiDispatcher ?? throw new ArgumentNullException(nameof(uiDispatcher));
        }

        public readonly record struct TryResult<T>(bool Ok, T Value);

        public static Task<TryResult<T>> TrySyncAsAsync<T>(Func<T> func, string? operationName, bool shutdownServer = true, T fallback = default)
            => TryCatchAsync<T>(() => Task.Run(func), operationName, shutdownServer, fallback);

        public static Task<bool> TrySyncAsAsync(Action action, string? operationName, bool shutdownServer)
            => TryCatchAsync(() => Task.Run(action), operationName, shutdownServer);

        private static async Task<bool> TryCatchAsync(Func<Task> action, string? operationName, bool shutdownServer)
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

        private static async Task<TryResult<T>> TryCatchAsync<T>(Func<Task<T>> func, string? operationName, bool shutdownServer, T fallback = default)
        {
            try
            {
                return await new HostOperationBoundary().RunAsync(operationName ?? "Desktop operation", async () =>
                {
                    App.ReloadConfiguration();
                    return await RunBusyAsync(func, operationName, shutdownServer);
                });
            }
            catch (OperationCanceledException exception)
            {
                MessageLogger.Warning($"{operationName} canceled: {exception.Message}");
                return new TryResult<T>(false, fallback);
            }
            catch (Exception exception)
            {
                MessageLogger.Error($"{operationName} failed: {exception.Message}");
                return new TryResult<T>(false, fallback);
            }
        }

        private static async Task<TryResult<T>> RunBusyAsync<T>(Func<Task<T>> func, string? operationName, bool shutdownServer)
        {
            var minVisibleMs = 2000;
            var busy = Singleton<BusyHandler>.Instance;
            var operationId = Guid.NewGuid();
            var stopWatch = Stopwatch.StartNew();

            using (OperationScope.Begin(operationId))
            {
                var result = await HostOperationExecution.RunAsync(operationName ?? "Desktop operation", async () =>
                {
                    await Ui.EnqueueAsync(() =>
                    {
                        busy.Start(operationName, operationId);
                    }).ConfigureAwait(false);

                    // Let UI paint once
                    await Task.Yield();

                    if (shutdownServer)
                    {
                        await Task.Run(() => WorkflowContext.ServiceCall(ServiceHelper.StopW3SVC)).ConfigureAwait(false);
                    }

                    if (!operationName.IsNullOrEmpty())
                        MessageLogger.Highlight($"▶ {operationName} started");

                    Singleton<W3cServiceState>.Instance.InOperation = true;

                    return await func().ConfigureAwait(true);
                }, async () =>
                {
                    Singleton<W3cServiceState>.Instance.InOperation = false;

                    try
                    {
                        await Task.Run(ServiceHelper.StartW3SVC).ConfigureAwait(false);
                    }
                    finally
                    {
                        stopWatch.Stop();
                        var remainingMilliseconds = minVisibleMs - (int)stopWatch.ElapsedMilliseconds;
                        if (remainingMilliseconds > 0)
                            await Task.Delay(remainingMilliseconds).ConfigureAwait(true);

                        await Ui.EnqueueAsync(() => busy.Stop(operationId)).ConfigureAwait(false);
                    }
                });
                return new TryResult<T>(true, result);
            }
        }
    }
}
