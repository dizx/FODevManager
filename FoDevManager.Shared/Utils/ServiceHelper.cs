using FODevManager.Messages;
using FODevManager.Shared.Utils.FODevManager.WinUI.Services;
using FODevManager.Shared.Utils;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.ServiceProcess;

namespace FODevManager.Utils
{
    public static class ServiceHelper
    {
        public static void RefreshW3SVCState(Func<bool>? isRunning = null)
        {
            var state = Singleton<W3cServiceState>.Instance;
            lock (state.SyncRoot)
            {
                if (state.InOperation)
                    throw new InvalidOperationException("Cannot refresh W3SVC state during another operation");

                state.IsRunning = (isRunning ?? IsW3cRunning)();
            }
        }

        public static bool IsW3cRunning()
        {
            using var serviceController = new ServiceController("W3SVC");
            return serviceController.Status == ServiceControllerStatus.Running
                || serviceController.Status == ServiceControllerStatus.StartPending;
        }

        public static void StopW3SVC()
            => ChangeW3SVCState(running: false);

        public static void StartW3SVC()
            => ChangeW3SVCState(running: true);

        public static void ChangeW3SVCState(bool running,
            Func<string, (int ExitCode, string Output, string Error)>? execute = null)
        {
            var w3cServiceState = Singleton<W3cServiceState>.Instance;

            lock (w3cServiceState.SyncRoot)
            {
                if (w3cServiceState.InOperation || w3cServiceState.IsRunning == running)
                    return;
            }

            var action = running ? "start" : "stop";
            MessageLogger.Info($"Requesting W3SVC {action}..");

            try
            {
                var result = execute != null
                    ? execute(action)
                    : RunServiceCommandAsync(action).GetAwaiter().GetResult();
                if (result.ExitCode != 0)
                    throw new InvalidOperationException(
                        $"net {action} W3SVC exited with code {result.ExitCode}. {result.Output.Trim()} {result.Error.Trim()}");

                lock (w3cServiceState.SyncRoot)
                    w3cServiceState.IsRunning = running;

                MessageLogger.Info(running ? "W3SVC started" : "W3SVC stopped");
            }
            catch (Exception ex)
            {
                MessageLogger.Error($"Failed to {action} W3SVC: {ex.Message}");
                if (!running && Singleton<Engine>.Instance.EnvironmentType == EnvironmentType.Console)
                    throw;
            }
        }

        private static async Task<(int ExitCode, string Output, string Error)> RunServiceCommandAsync(string action)
        {
            using var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = "net",
                    Arguments = $"{action} W3SVC",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                }
            };

            process.Start();
            // Drain both pipes while waiting so a full redirected buffer cannot block net
            var output = process.StandardOutput.ReadToEndAsync();
            var error = process.StandardError.ReadToEndAsync();
            await Task.WhenAll(output, error, process.WaitForExitAsync()).ConfigureAwait(false);
            return (process.ExitCode, await output.ConfigureAwait(false), await error.ConfigureAwait(false));
        }
        

        public static void OpenUrl(string url)
        {
            try
            {
                if (url.IsNullOrEmpty())
                {
                    MessageLogger.Warning("⚠ No URL specified");
                    return;
                }

                Process.Start(new ProcessStartInfo
                {
                    FileName = url,
                    UseShellExecute = true
                });
                MessageLogger.Info($"🌐 Opening: {url}");
            }
            catch (System.Exception ex)
            {
                MessageLogger.Error($"❌ Failed to open URL: {ex.Message}");
            }
        }
    }
}
