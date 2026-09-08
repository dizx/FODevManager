using FODevManager.Messages;
using FODevManager.Utils;

namespace FODevManager.Tests;

[TestFixture]
[NonParallelizable]
public class WebConfigHelperTests
{
    [TestCase(null)]
    [TestCase("<configuration><appSettings /></configuration>")]
    [TestCase("invalid XML")]
    public void FailedDatabaseUpdatePublishesError(string? content)
    {
        var path = Path.Combine(Path.GetTempPath(), $"fodev-web-{Guid.NewGuid():N}.config");
        try
        {
            if (content != null) File.WriteAllText(path, content);
            var errors = new List<Message>();
            using var subscription = MessageBus.Subscribe(message =>
            {
                if (message.Type == MessageType.Error) errors.Add(message);
            });
            WebConfigHelper.UpdateWebConfigDatabase("NewDatabase", path);
            Assert.That(errors, Has.Count.EqualTo(1));
            if (content != null) Assert.That(File.ReadAllText(path), Is.EqualTo(content));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Test]
    public void DatabaseUpdateAndNoChangeDoNotPublishErrors()
    {
        var path = Path.Combine(Path.GetTempPath(), $"fodev-web-{Guid.NewGuid():N}.config");
        try
        {
            File.WriteAllText(path, "<configuration><appSettings><add key=\"DataAccess.Database\" value=\"Old\" /></appSettings></configuration>");
            var errors = new List<Message>();
            using var subscription = MessageBus.Subscribe(message =>
            {
                if (message.Type == MessageType.Error) errors.Add(message);
            });
            WebConfigHelper.UpdateWebConfigDatabase("NewDatabase", path);
            var updated = File.ReadAllText(path);
            WebConfigHelper.UpdateWebConfigDatabase("NewDatabase", path);
            Assert.That(updated, Does.Contain("value=\"NewDatabase\""));
            Assert.That(File.ReadAllText(path), Is.EqualTo(updated));
            Assert.That(errors, Is.Empty);
        }
        finally
        {
            File.Delete(path);
        }
    }
}
