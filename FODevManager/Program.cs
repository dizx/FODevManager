using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using FODevManager.Services;
using System;
using System.IO;
using FODevManager.Utils;

class Program
{
    static void Main(string[] args)
    {
        var commandParser = new CommandParser(args);
        if (!commandParser.IsValid)
        {
            Console.WriteLine("Usage: fodev.exe -profile \"ProfileName\" <command> [options]");
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

        if (commandParser.ModelName == null)
        {
            switch (commandParser.Command)
            {
                case "create":
                    profileService.CreateProfile(commandParser.ProfileName);
                    break;
                case "delete":
                    profileService.DeleteProfile(commandParser.ProfileName);
                    break;
                case "check":
                    profileService.CheckProfile(commandParser.ProfileName);
                    break;
                case "list":
                    profileService.ListProfiles();
                    break;
                case "deploy-all":
                    modelService.DeployAllUndeployedModels(commandParser.ProfileName);
                    break;
                default:
                    Console.WriteLine("Invalid profile command.");
                    break;
            }
        }
        else
        {
            switch (commandParser.Command)
            {
                case "add":                    
                    profileService.AddEnvironment(commandParser.ProfileName, commandParser.ModelName, commandParser.FilePath);
                    break;
                case "remove":
                    profileService.RemoveModelFromProfile(commandParser.ProfileName, commandParser.ModelName);
                    break;
                case "deploy":
                    modelService.DeployModel(commandParser.ProfileName, commandParser.ModelName);
                    break;
                case "check":
                    modelService.CheckModelDeployment(commandParser.ProfileName, commandParser.ModelName);
                    break;
                default:
                    Console.WriteLine("Invalid model command.");
                    break;
            }
        }

    }
}
