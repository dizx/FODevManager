using FODevManager.Messages;
using System;
using System.IO;
using System.Text.Json;

namespace FODevManager.Utils
{
    public sealed class ProfileSyncState
    {
        public string MessageId { get; set; } = string.Empty;
        public string ProfileName { get; set; } = string.Empty;
        public string SourceApp { get; set; } = string.Empty;
        public DateTime UpdatedUtc { get; set; }
    }

    public static class ProfileSyncBridge
    {
        public const string SourceFoDev = "FODev";
        public const string SourceEasyGit = "EasyGit";

        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            WriteIndented = false
        };

        public static bool Publish(string sourceApp, string profileName, out string messageId)
        {
            messageId = string.Empty;

            if (string.IsNullOrWhiteSpace(sourceApp) || string.IsNullOrWhiteSpace(profileName))
                return false;

            try
            {
                var state = new ProfileSyncState
                {
                    MessageId = Guid.NewGuid().ToString("N"),
                    SourceApp = sourceApp.Trim(),
                    ProfileName = profileName.Trim(),
                    UpdatedUtc = DateTime.UtcNow
                };

                var folder = Path.GetDirectoryName(GetSyncFilePath()) ?? string.Empty;
                if (!string.IsNullOrWhiteSpace(folder))
                    Directory.CreateDirectory(folder);

                var json = JsonSerializer.Serialize(state, JsonOptions);
                File.WriteAllText(GetSyncFilePath(), json);
                messageId = state.MessageId;
                return true;
            }
            catch (Exception exception)
            {
                MessageLogger.Warning($"Profile sync publish failed: {exception.Message}");
                return false;
            }
        }

        public static bool TryRead(out ProfileSyncState state)
        {
            state = new ProfileSyncState();

            try
            {
                var path = GetSyncFilePath();
                if (!File.Exists(path))
                    return false;

                var json = File.ReadAllText(path);
                var parsed = JsonSerializer.Deserialize<ProfileSyncState>(json);
                if (parsed == null || string.IsNullOrWhiteSpace(parsed.MessageId))
                    return false;

                state = parsed;
                return true;
            }
            catch (Exception exception)
            {
                MessageLogger.Warning($"Profile sync read failed: {exception.Message}");
                return false;
            }
        }

        private static string GetSyncFilePath()
        {
            var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            return Path.Combine(appData, "FODevManager", "interapp-profile-sync.json");
        }
    }
}
