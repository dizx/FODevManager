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
        if (args.Length < 2)
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

        string profileName = args[1];
        string command = args.Length > 2 ? args[2].ToLower() : args[1];

        switch (command)
        {
            case "create":
                profileService.CreateProfile(profileName);
                break;

            case "add":
                if (args.Length > 4 && args[2] == "-model")
                    profileService.AddEnvironment(profileName, args[3], args[4]);
                else
                    Console.WriteLine("Usage: fodev.exe -profile \"ProfileName\" -model \"ModelName\" add \"Path\"");
                break;

            case "check":
                if (args.Length == 3)
                    profileService.CheckProfile(profileName);
                else if (args.Length > 4 && args[2] == "-model")
                    modelService.CheckModelDeployment(profileName, args[3]);
                else
                    Console.WriteLine("Usage: fodev.exe -profile \"ProfileName\" check OR -model \"ModelName\" check");
                break;

            case "deploy":
                if (args.Length > 4 && args[2] == "-model")
                    modelService.DeployModel(profileName, args[3]);
                else
                    Console.WriteLine("Usage: fodev.exe -profile \"ProfileName\" -model \"ModelName\" deploy");
                break;
            
            case "delete":
                profileService.DeleteProfile(profileName);
                break;

            case "remove":
                if (args.Length > 4 && args[2] == "-model")
                    profileService.RemoveModelFromProfile(profileName, args[3]);
                else
                    Console.WriteLine("Usage: fodev.exe -profile \"MyProfile\" -model \"MyModel\" remove");
                break;

            case "list":
                if (args.Length == 2)
                {
                    profileService.ListProfiles();
                }
                else if (args.Length > 3 && args[2] == "-models")
                {
                    profileService.ListModelsInProfile(args[3]);
                }
                else
                {
                    Console.WriteLine("Usage:");
                    Console.WriteLine("  fodev.exe list               # Lists all profiles");
                    Console.WriteLine("  fodev.exe list -models \"MyProfile\" # Lists models in a specific profile");
                }
                break;

            default:
                Console.WriteLine("Invalid command.");
                break;
        }
    }
}
