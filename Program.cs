using FODevManager.Services;

class Program
{
    static void Main(string[] args)
    {
        if (args.Length < 2)
        {
            Console.WriteLine("Usage: fodev.exe -profile \"ProfileName\" <command> [options]");
            return;
        }

        string profileName = args[1];
        string command = args.Length > 2 ? args[2].ToLower() : "";

        var profileService = new ProfileService();
        var modelService = new ModelDeploymentService();

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
