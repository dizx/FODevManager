using FODevManager.Messages;
using System;
using System.IO;
using System.Text;

namespace FODevManager.Shared.Utils
{
    public static class AppSettingsMigration
    {
        public static void RunOnStartup()
        {
            var configFilePath = Path.Combine(AppContext.BaseDirectory, "appsettings.json");

            try
            {
                if (!File.Exists(configFilePath))
                    return;

                var originalText = File.ReadAllText(configFilePath, Encoding.UTF8);

                // Cheap and safe enough: only migrate the key name token, keep the value untouched.
                // Example: "PeriTaskUrl": "https://.."  ->  "TaskUrl": "https://.."
                var migratedText = originalText.Replace("\"PeriTaskUrl\"", "\"TaskUrl\"", StringComparison.Ordinal);

                if (string.Equals(originalText, migratedText, StringComparison.Ordinal))
                    return;

                File.WriteAllText(configFilePath, migratedText, Encoding.UTF8);

                MessageLogger.Highlight("Config migration applied: PeriTaskUrl -> TaskUrl");
            }
            catch (Exception exception)
            {
                // Migration must not block startup
                MessageLogger.Warning($"Config migration failed. Continuing without migration. {exception.Message}");
            }
        }
    }
}
