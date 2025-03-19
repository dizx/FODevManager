using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using FODevManager.Services;
using System;
using System.IO;
using FODevManager.Utils;
using FoDevManager.Messages;

class Program
{
    static void Main(string[] args)
    {
        var commandParser = new CommandParser(args);

        var consoleSubscriber = new ConsoleSubscriber();
        
        if (!commandParser.IsValid)
        {
            MessageLogger.Write("Usage: fodev.exe -profile \"ProfileName\" <command> [options]");
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
                services.AddSingleton<ModelDeploymentService>();
                services.AddSingleton<VisualStudioSolutionService>();
            })
            .Build();
        
        // Resolve services
        var profileService = host.Services.GetRequiredService<ProfileService>();
        var modelService = host.Services.GetRequiredService<ModelDeploymentService>();

        if (commandParser.ModelName == null) //Commands on Profile
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

                default:
                    MessageLogger.Error($"Invalid model command: {commandParser.Command}");
                    break;
            }
        }
        else
        {
            switch (commandParser.Command) //Commands on Model
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
