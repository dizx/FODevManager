using FODevManager.Messages;
using FODevManager.Shared.Utils;
using FODevManager.Utils;

namespace FODevManager.Tests;

[TestFixture]
[NonParallelizable]
public class AppConfigWriterTests
{
    [TestCase(nameof(AppConfig.AzureArtifactsPat))]
    [TestCase(nameof(AppConfig.AzureArtifactsApiKey))]
    [TestCase("FutureSecretSetting")]
    [TestCase(nameof(AppConfig.CheckUncommittedBeforeSwitch))]
    public void UpdateSetting_DoesNotLogValues(string key)
    {
        const string secret = "test-secret-not-for-logs";
        var path = Path.Combine(AppContext.BaseDirectory, "appsettings.json");
        var original = File.Exists(path) ? File.ReadAllBytes(path) : null;
        var messages = new List<Message>();
        using var subscription = MessageBus.Subscribe(messages.Add);

        try
        {
            File.WriteAllText(path, "{}");
            var config = new AppConfig();
            var writer = new AppConfigWriter(config);
            if (key == nameof(AppConfig.CheckUncommittedBeforeSwitch))
            {
                Assert.Throws<FormatException>(() => writer.UpdateSetting(key, secret));
                Assert.That(messages.Any(message => message.Type == MessageType.Error), Is.True);
            }
            else
            {
                writer.UpdateSetting(key, secret);
                Assert.That(messages.Any(message => message.Content == $"Config updated: {key}"), Is.True);
                var saved = FileHelper.LoadJson<Dictionary<string, string>>(path);
                Assert.That(saved[key], Is.EqualTo(secret));
                if (key == nameof(AppConfig.AzureArtifactsPat))
                    Assert.That(config.AzureArtifactsPat, Is.EqualTo(secret));
                if (key == nameof(AppConfig.AzureArtifactsApiKey))
                    Assert.That(config.AzureArtifactsApiKey, Is.EqualTo(secret));
            }

            Assert.That(messages.All(message => !message.Content.Contains(secret)), Is.True);
        }
        finally
        {
            if (original == null)
                File.Delete(path);
            else
                File.WriteAllBytes(path, original);
        }
    }
}
