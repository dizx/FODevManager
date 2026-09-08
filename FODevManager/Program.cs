using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using FODevManager.Services;
using FODevManager.Utils;
using FODevManager.Messages;
using FODevManager.Models;
using Serilog;
using FODevManager.Logging;
using FODevManager.Shared.Utils;
using System.Text.Json;

internal class Program
{
    public static int Main(string[] args)
    {
        var result = 1;
        try
        {
            Singleton<Engine>.Instance.EnvironmentType = EnvironmentType.Console;
            using var consoleSubscriber = new ConsoleSubscriber();
            using var serilogSubscriber = new SerilogSubscriber();
            result = Execute(args, () =>
            {
                var logDirectory = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "FODevManager", "Logs");
                Directory.CreateDirectory(logDirectory);
                Log.Logger = new LoggerConfiguration()
                    .MinimumLevel.Information()
                    .WriteTo.File(Path.Combine(logDirectory, "fodev-.log"),
                        rollingInterval: RollingInterval.Day, retainedFileCountLimit: 10,
                        outputTemplate: "[{Timestamp:yyyy-MM-dd HH:mm:ss}] [{Level:u3}] {Message:lj}{NewLine}")
                    .CreateLogger();
                return BuildHost();
            });
        }
        catch (Exception ex)
        {
            MessageLogger.Error(ex.ToString());
            result = 1;
        }
        finally
        {
            try
            {
                Log.CloseAndFlush();
            }
            catch (Exception ex)
            {
                MessageLogger.Error(ex.ToString());
                result = 1;
            }
        }
        return result;
    }

    internal static IHost BuildHost(string? basePath = null, Action<IServiceCollection>? configureServices = null)
    {
        return Host.CreateDefaultBuilder()
            .ConfigureAppConfiguration((context, config) =>
            {
                config.Sources.Clear();
                config.SetBasePath(basePath ?? AppContext.BaseDirectory);
                config.AddJsonFile("appsettings.json", optional: true, reloadOnChange: false);
                config.AddJsonFile($"appsettings.{context.HostingEnvironment.EnvironmentName}.json", optional: true, reloadOnChange: false);
                config.AddEnvironmentVariables();
            })
            .ConfigureServices((context, services) =>
            {
                // The CLI builds packages locally and never publishes them
                services.AddSingleton(new AppConfig(context.Configuration) { PushDeployablePackageOnBuild = false });
                services.AddSingleton<ProfileService>();
                services.AddSingleton<FileService>();
                services.AddSingleton<ProfilesContainer>();
                services.AddSingleton<DeployablePackageService>();
                services.AddSingleton<ModelDeploymentService>();
                services.AddSingleton<VisualStudioSolutionService>();
                services.AddSingleton<ModelVersionService>();
                services.AddSingleton<IDeploymentLedgerService, DeploymentLedgerService>();
                services.AddSingleton<IDirectoryLinkService, DirectoryLinkService>();
                configureServices?.Invoke(services);
            })
            .Build();
    }

    internal static int Execute(string[] args, Func<IHost> buildHost, Action? refreshServiceState = null)
    {
        var errors = 0;
        using var errorSubscription = MessageBus.Subscribe(message =>
        {
            if (message.Type == MessageType.Error)
                Interlocked.Increment(ref errors);
        });
        try
        {
            var parser = CommandParser.Parse(args);
            if (parser.IsHelpRequested)
                return 0;
            if (!parser.IsValid)
                return 2;

            using (var host = buildHost())
            {
                if (errors != 0)
                    return 1;
                if (!RunCommand(parser, host.Services, () => Volatile.Read(ref errors) != 0,
                    refreshServiceState ?? (() => ServiceHelper.RefreshW3SVCState())))
                {
                    if (errors == 0)
                        MessageLogger.Error($"Command '{parser.Command}' did not complete successfully");
                    return 1;
                }
            }
            return errors == 0 ? 0 : 1;
        }
        catch (Exception ex)
        {
            MessageLogger.Error(ex.ToString());
            return 1;
        }
    }

    private static bool RunCommand(CommandParser command, IServiceProvider services, Func<bool> hasErrors, Action refreshServiceState)
    {
        var files = services.GetRequiredService<FileService>();
        var profileName = command.ProfileName;
        var modelName = command.ModelName;

        // Read-only commands avoid ProfileService.LoadProfile and deployment service construction
        if (command.Command == "list" && profileName == null)
        {
            MessageLogger.Info("Installed profiles:");
            foreach (var name in files.GetAllProfileNames())
                MessageLogger.Info($"- {name}");
            return true;
        }

        if (command.Command is "show" or "repos" or "export" or "solution-path" or "solution-ensure" or "list")
        {
            var profile = files.LoadProfile(profileName);
            switch (command.Command)
            {
                case "show":
                case "list":
                    MessageLogger.Info($"Profile: {profile.ProfileName}");
                    MessageLogger.Info($"Database: {profile.DatabaseName}");
                    MessageLogger.Info($"Active: {profile.IsActive}");
                    MessageLogger.Info($"Solution: {services.GetRequiredService<VisualStudioSolutionService>().GetSolutionFilePath(profile)}");
                    var models = modelName == null ? profile.AllModels :
                        new List<ProfileEnvironmentModel> { profile.FindModel(modelName) ?? throw new InvalidOperationException($"Model '{modelName}' not found") };
                    foreach (var model in models)
                    {
                        MessageLogger.Info($"Model: {model.ModelName} ({model.ModelType})");
                        MessageLogger.Info($"  Repository: {profile.FindRepositoryForModel(model)?.DisplayName ?? "Standalone"}");
                        MessageLogger.Info($"  Root: {model.ModelRootFolder}");
                        MessageLogger.Info($"  Source: {model.MetadataFolder}; compiled: {model.CompiledModelFolder}");
                        MessageLogger.Info($"  Project: {model.ProjectFilePath}");
                        MessageLogger.Info($"  Package: {model.PackageId} {model.PackageVersion}");
                        MessageLogger.Info($"  Deployed (saved state): {model.IsDeployed}");
                    }
                    return true;
                case "repos":
                    MessageLogger.Info($"Repositories in '{profile.ProfileName}' ({profile.Repositories.Count}):");
                    foreach (var repo in profile.Repositories)
                    {
                        MessageLogger.Info($"- {repo.DisplayName} [{repo.RepoId}]");
                        MessageLogger.Info($"  Root: {repo.RepoRootFolder}");
                        MessageLogger.Info($"  Git: {repo.GitUrl}");
                        MessageLogger.Info($"  Preferred branch: {repo.PreferredBranch}; last known branch: {repo.LastKnownBranch}");
                        MessageLogger.Info($"  Models: {string.Join(", ", repo.Models.Select(model => model.ModelName))}");
                    }
                    return true;
                case "export":
                    var json = JsonSerializer.Serialize(ExportProfileMapper.ToExport(profile), new JsonSerializerOptions { WriteIndented = true });
                    using (var output = new FileStream(command.FilePath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                    using (var writer = new StreamWriter(output))
                        writer.Write(json);
                    MessageLogger.Info($"Exported profile to '{Path.GetFullPath(command.FilePath)}'");
                    return true;
                case "solution-path":
                    MessageLogger.Info(services.GetRequiredService<VisualStudioSolutionService>().GetSolutionFilePath(profile));
                    return true;
                case "solution-ensure":
                    var solutions = services.GetRequiredService<VisualStudioSolutionService>();
                    var path = solutions.CreateSolutionFile(profile);
                    if (string.IsNullOrWhiteSpace(path) || !File.Exists(path) || hasErrors())
                        return false;
                    foreach (var model in profile.AllModels.Where(model => model.ModelType == ModelType.Source))
                    {
                        solutions.AddProjectToSolution(profile, model);
                        if (hasErrors())
                            return false;
                    }
                    MessageLogger.Info(path);
                    return true;
            }
        }

        var profiles = services.GetRequiredService<ProfileService>();
        var deployment = services.GetRequiredService<ModelDeploymentService>();
        if (modelName != null && command.Command != "add")
        {
            var profile = files.LoadProfile(profileName);
            if (profile.FindModel(modelName) == null)
                throw new InvalidOperationException($"Model '{modelName}' not found in profile '{profileName}'");
        }

        switch (command.Command)
        {
            case "create": profiles.CreateProfile(profileName); break;
            case "import": return profiles.ImportProfile(command.FilePath) != null;
            case "delete":
            case "undeploy":
                var removalProfile = files.LoadProfile(profileName);
                var applicableModels = modelName == null ? removalProfile.AllModels :
                    new List<ProfileEnvironmentModel> { removalProfile.FindModel(modelName)! };
                var deployedModels = new List<ProfileEnvironmentModel>();
                // Validate the entire set before service control or the first destructive action
                foreach (var model in applicableModels)
                    if (VerifyDeploymentOwnership(removalProfile, model, services))
                        deployedModels.Add(model);
                if (deployedModels.Count != 0)
                {
                    refreshServiceState();
                    foreach (var model in deployedModels)
                    {
                        deployment.UnDeployModel(profileName, model.ModelName);
                        if (hasErrors() || services.GetRequiredService<IDirectoryLinkService>()
                            .Exists(Path.Combine(services.GetRequiredService<AppConfig>().DeploymentBasePath, model.ModelName)))
                            return false;
                    }
                }
                if (command.Command == "delete")
                {
                    files.DeleteProfile(profileName);
                    MessageLogger.Info($"Profile '{profileName}' removed");
                }
                return true;
            case "check":
                if (modelName == null) profiles.CheckProfile(profileName);
                else deployment.CheckModelDeployment(profileName, modelName, true);
                break;
            case "deploy":
                if (modelName != null)
                {
                    refreshServiceState();
                    return deployment.DeployModel(profileName, modelName);
                }
                if (files.LoadProfile(profileName).AllModels.All(deployment.IsModelActuallyDeployed))
                {
                    MessageLogger.Info("All models are already deployed");
                    return true;
                }
                refreshServiceState();
                return deployment.DeployAllUndeployedModels(profileName)
                    && files.LoadProfile(profileName).AllModels.All(deployment.IsModelActuallyDeployed);
            case "open-vs": profiles.OpenVisualStudioSolution(profileName); break;
            case "git-fetch": profiles.GitFetchLatest(profileName); break;
            case "switch":
                files.LoadProfile(profileName);
                if (profiles.GetActiveProfileName() == profileName) return true;
                refreshServiceState();
                return profiles.SwitchProfile(profileName);
            case "db-set":
                if (files.LoadProfile(profileName).IsActive) refreshServiceState();
                profiles.SetDatabaseName(profileName, command.DatabaseName);
                break;
            case "db-apply":
                refreshServiceState();
                profiles.ApplyDatabase(profileName);
                break;
            case "add": profiles.AddModel(profileName, modelName!, command.FilePath); break;
            case "remove":
                if (!PrepareRemoval(files.LoadProfile(profileName), modelName!, services, hasErrors, refreshServiceState))
                    return false;
                profiles.RemoveModelFromProfile(profileName, modelName!);
                return files.LoadProfile(profileName).FindModel(modelName!) == null;
            case "git-status": return deployment.CheckIfGitRepository(profileName, modelName!);
            case "git-open": deployment.OpenGitRepositoryUrl(profileName, modelName!); break;
            case "peri": return deployment.AssignTask(profileName, modelName!, command.FilePath, "", switchBranch: false);
            case "package-build":
                services.GetRequiredService<AppConfig>().PushDeployablePackageOnBuild = false;
                return profiles.BuildDeployableNugetPackage(profileName, modelName!);
            default: throw new InvalidOperationException($"Unsupported command '{command.Command}'");
        }
        return true;
    }

    private static bool PrepareRemoval(ProfileModel profile, string modelName, IServiceProvider services, Func<bool> hasErrors, Action refreshServiceState)
    {
        var model = profile.FindModel(modelName)!;
        if (model.ModelType == ModelType.CompiledNuget)
            throw new InvalidOperationException("CLI removal of a NuGet package model is unsafe because shared removal can affect other models in the package. Use the UI package workflow");

        if (!VerifyDeploymentOwnership(profile, model, services))
            return true;
        refreshServiceState();
        services.GetRequiredService<ModelDeploymentService>().UnDeployModel(profile.ProfileName, model.ModelName);
        return !hasErrors() && !services.GetRequiredService<IDirectoryLinkService>()
            .Exists(Path.Combine(services.GetRequiredService<AppConfig>().DeploymentBasePath, model.ModelName));
    }

    private static bool VerifyDeploymentOwnership(ProfileModel profile, ProfileEnvironmentModel model, IServiceProvider services)
    {
        var modelName = model.ModelName;
        var config = services.GetRequiredService<AppConfig>();
        var links = services.GetRequiredService<IDirectoryLinkService>();
        if (string.IsNullOrWhiteSpace(modelName) || modelName != Path.GetFileName(modelName)
            || modelName.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0
            || modelName.EndsWith('.') || modelName.EndsWith(' '))
            throw new InvalidOperationException("Invalid model name for deployment removal");
        var linkPath = Path.Combine(config.DeploymentBasePath, modelName);
        if (!links.Exists(linkPath))
        {
            if (File.Exists(linkPath) || new DirectoryInfo(linkPath).LinkTarget != null)
                throw new InvalidOperationException($"Refusing to remove '{modelName}': deployment path is a file or broken link");
            return false;
        }

        var target = links.ResolveLinkTarget(linkPath);
        var source = model.ModelType == ModelType.Source ? model.MetadataFolder : model.CompiledModelFolder;
        var records = services.GetRequiredService<IDeploymentLedgerService>().LoadRecords()
            .Where(record => record.ModelName.SameAs(modelName)).ToList();
        if (target == null || string.IsNullOrWhiteSpace(source) || records.Count != 1
            || records[0].IsUnmanaged || !records[0].ProfileName.SameAs(profile.ProfileName)
            || !SamePath(target, source) || !SamePath(records[0].SourcePath, source))
            throw new InvalidOperationException($"Refusing to remove '{modelName}': deployment is not a verified link owned by profile '{profile.ProfileName}'");

        return true;
    }

    private static bool SamePath(string left, string right) =>
        !string.IsNullOrWhiteSpace(left) && !string.IsNullOrWhiteSpace(right)
        && string.Equals(Path.TrimEndingDirectorySeparator(Path.GetFullPath(left)),
            Path.TrimEndingDirectorySeparator(Path.GetFullPath(right)), StringComparison.OrdinalIgnoreCase);
}
