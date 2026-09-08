using FODevManager.Utils;
using Microsoft.Extensions.Configuration;

namespace FODevManager.Tests;

[TestFixture]
public class AppConfigTests
{
    [Test]
    public void Constructor_MissingToggles_PreservesDefaults()
    {
        var config = new AppConfig(new ConfigurationBuilder().Build());

        Assert.That(config.CheckUncommittedBeforeSwitch, Is.True);
        Assert.That(config.PushDeployablePackageOnBuild, Is.False);
    }

    [TestCase(null)]
    [TestCase("")]
    [TestCase("not-a-bool")]
    public void Constructor_InvalidToggles_PreservesDefaults(string? value)
    {
        var config = CreateConfig(value);

        Assert.That(config.CheckUncommittedBeforeSwitch, Is.True);
        Assert.That(config.PushDeployablePackageOnBuild, Is.False);
    }

    [TestCase("true", true)]
    [TestCase("false", false)]
    public void Constructor_ExplicitToggles_UsesValues(string value, bool expected)
    {
        var config = CreateConfig(value);

        Assert.That(config.CheckUncommittedBeforeSwitch, Is.EqualTo(expected));
        Assert.That(config.PushDeployablePackageOnBuild, Is.EqualTo(expected));
    }

    [TestCase("true", true)]
    [TestCase("false", false)]
    public void Constructor_EnvironmentToggles_ExpandsValues(string value, bool expected)
    {
        var variable = $"FODevManager_ConfigTest_{Guid.NewGuid():N}";
        try
        {
            Environment.SetEnvironmentVariable(variable, value);
            var config = CreateConfig($"%{variable}%");

            Assert.That(config.CheckUncommittedBeforeSwitch, Is.EqualTo(expected));
            Assert.That(config.PushDeployablePackageOnBuild, Is.EqualTo(expected));
        }
        finally
        {
            Environment.SetEnvironmentVariable(variable, null);
        }
    }

    private static AppConfig CreateConfig(string? value)
    {
        return new AppConfig(new ConfigurationBuilder().AddInMemoryCollection(
            new Dictionary<string, string?>
            {
                [nameof(AppConfig.CheckUncommittedBeforeSwitch)] = value,
                [nameof(AppConfig.PushDeployablePackageOnBuild)] = value
            }).Build());
    }
}
