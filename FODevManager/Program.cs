using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using FODevManager.Services;
using FODevManager.Utils;
using FODevManager.Messages;
using Serilog;
using FODevManager.Logging;
using FODevManager.Shared.Utils;


class Program
{
    static void Main(string[] args)
    {
        Singleton<Engine>.Instance.EnvironmentType = EnvironmentType.Console;

        string logDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "FODevManager", "Logs");

        Directory.CreateDirectory(logDirectory);

        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Information()
            .WriteTo.File(
                path: Path.Combine(logDirectory, "fodev-.log"),
                rollingInterval: RollingInterval.Day,
                retainedFileCountLimit: 10,
                outputTemplate: "[{Timestamp:yyyy-MM-dd HH:mm:ss}] [{Level:u3}] {Message:lj}{NewLine}")
            .CreateLogger();

        var consoleSubscriber = new ConsoleSubscriber();
        var serilogSubscriber = new SerilogSubscriber();

        var commandParser = CommandParser.Parse(args);
        
        if (!commandParser.IsValid)
        {
            return;
        }


        // Build the Host with Configuration and DI
        using IHost host = Host.CreateDefaultBuilder()
            .ConfigureAppConfiguration((context, config) =>
            {
                config.SetBasePath(AppContext.BaseDirectory);
                config.AddJsonFile("appsettings.json", optional: false, reloadOnChange: true);
            })
            .ConfigureServices((context, services) =>
            {   
                IConfiguration configuration = context.Configuration;

                services.AddSingleton(new AppConfig(configuration));
                services.AddSingleton<ProfileService>();
                services.AddSingleton<FileService>();
                services.AddSingleton<ModelDeploymentService>();
                services.AddSingleton<VisualStudioSolutionService>();
            })
            .Build();

        RunCommand(commandParser, host);

    }

    private static void RunCommand(CommandParser commandParser, IHost host)
    {
        var profileService = host.Services.GetRequiredService<ProfileService>();
        var modelService = host.Services.GetRequiredService<ModelDeploymentService>();

        if (commandParser.ModelName == null && !commandParser.Command.Equals("add")) // Profile level
        {
            switch (commandParser.Command)
            {
                case "create":
                    TryCatch(() => profileService.CreateProfile(commandParser.ProfileName));
                    break;
                case "delete":
                    TryCatch(() => profileService.DeleteProfile(commandParser.ProfileName));
                    break;
                case "check":
                    TryCatch(() => profileService.CheckProfile(commandParser.ProfileName));
                    break;
                case "list":
                    if (commandParser.ProfileName == null)
                        TryCatch(() => profileService.ListProfiles());
                    else
                        TryCatch(() => profileService.ListModelsInProfile(commandParser.ProfileName));
                    break;
                case "deploy":
                    TryCatch(() => modelService.DeployAllUndeployedModels(commandParser.ProfileName));
                    break;
                case "undeploy":
                    TryCatch(() => modelService.UnDeployAllModels(commandParser.ProfileName));
                    break;
                case "open-vs":
                    TryCatch(() => profileService.OpenVisualStudioSolution(commandParser.ProfileName));
                    break;
                case "git-fetch":
                    TryCatch(() => profileService.GitFetchLatest(commandParser.ProfileName));
                    break;
                case "switch":
                    TryCatch(() => profileService.SwitchProfile(commandParser.ProfileName));
                    break;
                case "db-set":
                    TryCatch(() => profileService.SetDatabaseName(commandParser.ProfileName, commandParser.DatabaseName));
                    break;
                case "db-apply":
                    TryCatch(() => profileService.ApplyDatabase(commandParser.ProfileName));
                    break;
                default:
                    MessageLogger.Error($"Invalid profile command: {commandParser.Command}");
                    break;
            }
        }
        else // Model level
        {
            switch (commandParser.Command)
            {
                case "add":
                    TryCatch(() => profileService.AddEnvironment(commandParser.ProfileName, commandParser.ModelName, commandParser.FilePath));
                    break;
                case "remove":
                    TryCatch(() => profileService.RemoveModelFromProfile(commandParser.ProfileName, commandParser.ModelName));
                    break;
                case "deploy":
                    TryCatch(() => modelService.DeployModel(commandParser.ProfileName, commandParser.ModelName));
                    break;
                case "undeploy":
                    TryCatch(() => modelService.UnDeployModel(commandParser.ProfileName, commandParser.ModelName));
                    break;
                case "check":
                    TryCatch(() => modelService.CheckModelDeployment(commandParser.ProfileName, commandParser.ModelName));
                    break;
                case "git-status":
                    TryCatch(() => modelService.CheckIfGitRepository(commandParser.ProfileName, commandParser.ModelName));
                    break;
                case "git-open":
                    TryCatch(() => modelService.OpenGitRepositoryUrl(commandParser.ProfileName, commandParser.ModelName));
                    break;
                default:
                    MessageLogger.Error($"Invalid model command: {commandParser.Command}");
                    break;
            }
        }
    }




    private static void TryCatch(Action asyncFunc)
    {
        try
        {
            asyncFunc();
        }
        catch (Exception ex)
        {
            MessageLogger.Error(ex.ToString());
        }
    }
}
