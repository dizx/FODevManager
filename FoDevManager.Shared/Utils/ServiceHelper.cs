using FODevManager.Messages;
using FODevManager.Shared.Utils.FODevManager.WinUI.Services;
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

        public static bool IsW3cRunning()
        {
            using var serviceController = new ServiceController("W3SVC");
            return serviceController.Status == ServiceControllerStatus.Running
                || serviceController.Status == ServiceControllerStatus.StartPending;
        }

        public static void StopW3SVC()
        {
            var w3cServiceState = Singleton<W3cServiceState>.Instance;

            lock (w3cServiceState.SyncRoot)
            {
                if (w3cServiceState.InOperation)
                {
                    return;
                }

                if (!w3cServiceState.IsRunning)
                {
                    MessageLogger.Info("⏳ W3C: stop skipped (already stopped).");
                    return;
                }
            }
            MessageLogger.Info("⏳ Stopping World Wide Web Publishing Service (W3SVC)...");

            try
            {
                Process process = new Process
                {
                    StartInfo = new ProcessStartInfo
                    {
                        FileName = "net",
                        Arguments = "stop W3SVC",
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        UseShellExecute = false,
                        CreateNoWindow = true
                    }
                };

                process.Start();
                process.WaitForExit();

                lock (w3cServiceState.SyncRoot)
                    w3cServiceState.IsRunning = false;

                MessageLogger.Info("✅ W3SVC stopped.");
            }
            catch (Exception ex)
            {
                MessageLogger.Error($"❌ Failed to stop W3SVC: {ex.Message}");
            }
            
        }
        public static void StartW3SVC()
        {
            var w3cServiceState = Singleton<W3cServiceState>.Instance;

            lock (w3cServiceState.SyncRoot)
            {
                if (w3cServiceState.InOperation)
                {
                    return;
                }

                if (w3cServiceState.IsRunning)
                {
                    MessageLogger.Info("⏳ W3C: start skipped (already running).");
                    return;
                }
            }
            MessageLogger.Info("🔄 Restarting World Wide Web Publishing Service (W3SVC)...");

            try
            {
                Process process = new Process
                {
                    StartInfo = new ProcessStartInfo
                    {
                        FileName = "net",
                        Arguments = "start W3SVC",
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        UseShellExecute = false,
                        CreateNoWindow = true
                    }
                };

                process.Start();
                process.WaitForExit();

                lock (w3cServiceState.SyncRoot)
                    w3cServiceState.IsRunning = true;

                MessageLogger.Info("✅ W3SVC restarted.");
            }
            catch (Exception ex)
            {
                MessageLogger.Error($"❌ Failed to start W3SVC: {ex.Message}");
            }
        }
        

        public static void OpenUrl(string url)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(url))
                {
                    MessageLogger.Warning("⚠ No URL specified.");
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
