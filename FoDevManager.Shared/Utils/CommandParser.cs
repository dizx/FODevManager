using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using System.Xml.Linq;
using FODevManager.Messages;

namespace FODevManager.Utils
{
    
    public class CommandParser
    {
        public string ProfileName { get; private set; }
        public string ModelName { get; private set; }
        public string Command { get; private set; }
        public string FilePath { get; private set; }
        public string DatabaseName { get; private set; }
        public bool IsValid { get; private set; }

        public CommandParser(string[] args)
        {
            ParseArguments(args);
        }
        public static CommandParser Parse(string[] args)
        {
            return new CommandParser(args);

        }

        private void ParseArguments(string[] args)
        {
            MessageLogger.LogOnly($"Arguments: {string.Join(" ", args)}");

            if (args.Length == 0 || args[0] == "help" || args[0] == "?")
            {
                if (args.Length == 1)
                {
                    ShowGeneralHelp();
                }
                else
                {
                    ShowCommandHelp(args[1]);
                }
                return;
            }

            if (args.Length < 2)
            {
                MessageLogger.Error("Insufficient arguments provided.");
                MessageLogger.Info("Usage: fodev.exe -profile \"ProfileName\" <command> [options]");
                IsValid = false;
                return;
            }

            if (args.Length == 0 || args[0] == "help" || args[0] == "?")
            {
                if (args.Length == 1)
                {
                    ShowGeneralHelp();
                }
                else
                {
                    ShowCommandHelp(args[1]);
                }
                return;
            }

            if (args.Length < 2)
            {
                MessageLogger.Error("Error: Insufficient arguments provided.");
                IsValid = false;
                return;
            }

            for (int i = 0; i < args.Length; i++)
            {
                switch (args[i])
                {
                    case "-profile":
                        if (i + 1 < args.Length)
                        {
                            string next = args[i + 1];

                            // If it's actually a command, treat it as command not profile name
                            if (IsCommand(next))
                            {
                                Command = next;
                                i++;
                            }
                            else
                            {
                                ProfileName = next;
                                i++;
                            }
                        }
                        break;

                    case "-model":
                        if (i + 1 < args.Length)
                        {
                            string next = args[i + 1];


                            if (IsCommand(next))
                            {
                                Command = next;
                                i++;
                            }
                            else
                            {
                                ModelName = next;
                                i++;
                            }
                        }
                        break;

                    case "db-set":
                        Command = "db-set";
                        if (i + 1 < args.Length)
                            DatabaseName = args[++i];
                        break;

                    case "db-apply":
                        Command = "db-apply";
                        break;

                    case "switch":
                        Command = "switch";
                        break;

                    default:
                        if (Command == null)
                            Command = args[i];
                        else
                            FilePath = args[i];
                        break;
                }
            }



            // Ensure required arguments are present
            IsValid = (!string.IsNullOrEmpty(Command) && Command.Equals("list"))
                || (!string.IsNullOrEmpty(ProfileName) && !string.IsNullOrEmpty(Command));

            if (!IsValid)
            {
                if(!string.IsNullOrEmpty(ModelName))
                    MessageLogger.Info("Usage: fodev.exe -profile \"ProfileName\" -model \"ModelName\" <command> [options]");
                else
                    MessageLogger.Info("Usage: fodev.exe -profile \"ProfileName\" <command> [options]");
            }
        }

        private bool IsCommand(string value)
        {
            var knownCommands = new[] {
                "create", "delete", "check", "list",
                "add", "remove", "deploy", "undeploy",
                "git-check", "git-open", "git-status",
                "switch", "db-set", "db-apply", "peri"
        };

            return knownCommands.Contains(value.ToLower());
        }



        private static void ShowGeneralHelp()
        {
            MessageLogger.Info(@"
                    FODevManager - Dynamics 365 FO Developer Profile Manager

                    Usage:
                      fodev.exe -profile ""<ProfileName>"" <command> [options]

                    Available Commands:
                       create        Create a new profile
                      delete        Delete an existing profile
                      check         Validate a profile and its models
                      list          List all profiles or models
                      add           Add a model to a profile
                      remove        Remove a model from a profile
                      deploy        Deploy a model or all undeployed models
                      git-check     Check if a model is under Git
                      git-open      Open the Git remote URL in the browser
                      db-set        Set the database name for the profile
                      db-apply      Applies the db to web.config
                      switch        Switch from current profile to another


                    Use 'fodev.exe help <command>' for more information.
                    ");
        }

        private static void ShowCommandHelp(string command)
        {
            switch (command.ToLower())
            {
                case "create":
                    MessageLogger.Info("Usage: fodev.exe -profile \"MyProfile\" create\nCreates a new profile JSON and solution.");
                    break;
                case "delete":
                    MessageLogger.Info("Usage: fodev.exe -profile \"MyProfile\" delete\nDeletes a profile and its .sln file.");
                    break;
                case "add":
                    MessageLogger.Info("Usage: fodev.exe -profile \"MyProfile\" -model \"MyModel\" add \"C:\\Path\\project.rnrproj\"\nAdds a model to a profile.");
                    break;
                case "remove":
                    MessageLogger.Info("Usage: fodev.exe -profile \"MyProfile\" -model \"MyModel\" remove\nRemoves a model from the profile.");
                    break;
                case "deploy":
                    MessageLogger.Info("Usage:\n  fodev.exe -profile \"MyProfile\" -model \"MyModel\" deploy\n  fodev.exe -profile \"MyProfile\" deploy\nDeploys model(s) to the FO metadata directory.");
                    break;
                case "check":
                    MessageLogger.Info("Usage: fodev.exe -profile \"MyProfile\" check\nValidates existence of models and their files.");
                    break;
                case "list":
                    MessageLogger.Info("Usage: fodev.exe -profile \"MyProfile\" list\nLists all models in a profile.");
                    break;
                case "git-check":
                    MessageLogger.Info("Usage: fodev.exe -profile \"MyProfile\" -model \"MyModel\" git-check\nChecks if the model is in a Git repository.");
                    break;
                case "git-open":
                    MessageLogger.Info("Usage: fodev.exe -profile \"MyProfile\" -model \"MyModel\" git-open\nOpens the Git remote URL in a browser.");
                    break;
                case "switch":
                    MessageLogger.Info("Usage: fodev.exe switch -profile \"MyProfile\" Switches to the specified profile safely.");
                    break;
                case "db-set":
                    MessageLogger.Info("Usage: fodev.exe -profile \"MyProfile\" db set \"DatabaseName\" Sets the database name to be used by this profile.");
                    break;
                case "db-apply":
                    MessageLogger.Info("Usage: fodev.exe -profile \"MyProfile\" db-apply\\nApplies the database setting from the profile to web.config.");
                    break;

                default:
                    MessageLogger.Info("Unknown command. Use 'fodev.exe help' to list all commands.");
                    break;
            }
        }
    }
}
