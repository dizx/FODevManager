using System.Text.Json;
using System.Text.Json.Nodes;
using FODevManager.Messages;
using FODevManager.Utils;
using Microsoft.Extensions.Configuration;

namespace FODevManager.Operations;

public sealed record SettingsUpdate
{
    public string? DefaultSourceDirectory { get; init; }
    public bool? CheckUncommittedBeforeSwitch { get; init; }
    public string? AzureArtifactsUsername { get; init; }
    public string? AzureArtifactsPat { get; init; }
    public string? AzureArtifactsApiKey { get; init; }
    public string? NuGetExecutablePath { get; init; }
    public bool? PushDeployablePackageOnBuild { get; init; }
    public string? PushDeployablePackageSource { get; init; }
    public string? ProfileStoragePath { get; init; }
    public string? DeploymentBasePath { get; init; }
    public string? DeployablePackages { get; init; }
    public string? TaskUrl { get; init; }
    public int? ModelIdBegin { get; init; }
    public int? ModelIdEnd { get; init; }
}

public sealed class SettingsOperations(AppConfig config, string basePath, string environmentName)
{
    public object Read() => new
    {
        config.DefaultSourceDirectory, config.CheckUncommittedBeforeSwitch, config.NuGetExecutablePath,
        config.PushDeployablePackageOnBuild, config.PushDeployablePackageSource, config.ProfileStoragePath,
        config.DeploymentBasePath, config.DeployablePackages, config.TaskUrl, config.ModelIdBegin, config.ModelIdEnd,
        CredentialsConfigured = !string.IsNullOrEmpty(config.AzureArtifactsPat) || !string.IsNullOrEmpty(config.AzureArtifactsApiKey)
    };

    public object Update(SettingsUpdate update)
    {
        ArgumentNullException.ThrowIfNull(update);
        var begin = update.ModelIdBegin ?? config.ModelIdBegin;
        var end = update.ModelIdEnd ?? config.ModelIdEnd;
        if (begin < 0 || end < begin) throw new ArgumentException("Model ID range is invalid");
        var path = Path.Combine(basePath, "appsettings.json");
        var document = File.Exists(path) ? JsonNode.Parse(File.ReadAllText(path)) as JsonObject : new JsonObject();
        if (document is null) throw new InvalidOperationException("Settings file must contain a JSON object");
        var changes = JsonSerializer.SerializeToNode(update)!.AsObject();
        foreach (var change in changes.Where(c => c.Value != null)) document[change.Key] = change.Value!.DeepClone();
        File.WriteAllText(path, document.ToJsonString(new JsonSerializerOptions { WriteIndented = true }).Replace("\r\n", "\n").Replace("\n", "\r\n"));
        Reload();
        MessageLogger.Info("Settings saved and effective configuration reloaded");
        return new { Effective = Read(), Precedence = "Environment variables override environment-specific JSON, which overrides appsettings.json", RestartRequired = false };
    }

    public void Reload()
    {
        var configuration = new ConfigurationBuilder().SetBasePath(basePath)
            .AddJsonFile("appsettings.json", true, false)
            .AddJsonFile($"appsettings.{environmentName}.json", true, false).AddEnvironmentVariables().Build();
        using var configurationLifetime = configuration as IDisposable;
        var fresh = new AppConfig(configuration);
        config.DefaultSourceDirectory = fresh.DefaultSourceDirectory;
        config.ProfileStoragePath = fresh.ProfileStoragePath;
        config.DeploymentBasePath = fresh.DeploymentBasePath;
        config.DeployablePackages = fresh.DeployablePackages;
        config.CheckUncommittedBeforeSwitch = fresh.CheckUncommittedBeforeSwitch;
        config.AzureArtifactsUsername = fresh.AzureArtifactsUsername;
        config.AzureArtifactsPat = fresh.AzureArtifactsPat;
        config.AzureArtifactsApiKey = fresh.AzureArtifactsApiKey;
        config.NuGetExecutablePath = fresh.NuGetExecutablePath;
        config.PushDeployablePackageOnBuild = fresh.PushDeployablePackageOnBuild;
        config.PushDeployablePackageSource = fresh.PushDeployablePackageSource;
        config.TaskUrl = fresh.TaskUrl;
        config.ModelIdBegin = fresh.ModelIdBegin;
        config.ModelIdEnd = fresh.ModelIdEnd;
    }
}
