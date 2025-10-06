using FODevManager.Messages;
using FODevManager.Utils;
using FODevManager.WinUI;
using FODevManager.WinUI.Framework;
using System;
using System.Threading.Tasks;

namespace FODevManager.WinUI.Framework
{
    public static class BusyOps
    {
        public static async Task<bool> TryCatchAsync(Func<Task> action, string operationName)
        {
            var busy = Singleton<BusyHandler>.Instance;
            var opId = Guid.NewGuid();

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
                    return false;
                }
                finally
                {
                    busy.Stop(opId);
                }
            }
        }

        // Overload for sync work
        public static Task<bool> TryCatchAsync(Action action, string operationName)
            => TryCatchAsync(() => Task.Run(action), operationName);
    }
}
