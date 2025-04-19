using FoDevManager.Messages;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace FoDevManager.Utils
{
    public static class ServiceHelper
    {
        public static void StopW3SVC()
        {
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

                MessageLogger.Info("✅ W3SVC stopped.");
            }
            catch (Exception ex)
            {
                MessageLogger.Error($"❌ Failed to stop W3SVC: {ex.Message}");
            }
        }
        public static void StartW3SVC()
        {
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

                MessageLogger.Info("✅ W3SVC restarted.");
            }
            catch (Exception ex)
            {
                MessageLogger.Error($"❌ Failed to start W3SVC: {ex.Message}");
            }
        }
    }
}
