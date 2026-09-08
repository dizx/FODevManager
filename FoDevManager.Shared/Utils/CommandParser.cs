using System;
using System.Collections.Generic;
using FODevManager.Messages;

namespace FODevManager.Utils
{
    public class CommandParser
    {
        private static readonly Dictionary<string, (string Usage, string Description)> Commands = new(StringComparer.OrdinalIgnoreCase)
        {
            ["create"] = ("-profile <ProfileName> create", "Creates a profile and solution"),
            ["delete"] = ("-profile <ProfileName> delete", "Undeploys verified profile-owned links and deletes the local profile; preserves the solution and source files"),
            ["import"] = ("-profile import <ProfileFile>", "Imports a profile and may clone repositories and create a solution"),
            ["list"] = ("[-profile <ProfileName>] list", "Lists profiles, or models in the specified profile; -profile list is also supported"),
            ["show"] = ("-profile <ProfileName> [-model <ModelName>] show", "Shows profile or model details"),
            ["export"] = ("-profile <ProfileName> export <OutputFile>", "Exports a portable profile; refuses to overwrite an existing file"),
            ["check"] = ("-profile <ProfileName> [-model <ModelName>] check", "Checks deployment state and saves profile state; profile checks may clone missing repositories and update external artifacts; not read-only"),
            ["add"] = ("-profile <ProfileName> -model [<ModelName>] add <ModelPath>", "Adds a model, inferring its name when omitted, and updates the solution"),
            ["remove"] = ("-profile <ProfileName> -model <ModelName> remove", "Removes a model from the profile"),
            ["deploy"] = ("-profile <ProfileName> [-model <ModelName>] deploy", "Creates deployment links for a model or all undeployed models"),
            ["undeploy"] = ("-profile <ProfileName> [-model <ModelName>] undeploy", "Removes deployment links for a model or all models"),
            ["git-status"] = ("-profile <ProfileName> -model <ModelName> git-status", "Checks the model's Git repository; git-check is an alias"),
            ["git-open"] = ("-profile <ProfileName> -model <ModelName> git-open", "Opens the Git remote URL in a browser"),
            ["git-fetch"] = ("-profile <ProfileName> git-fetch", "Fetches updates from profile repositories' remotes"),
            ["open-vs"] = ("-profile <ProfileName> open-vs", "Opens the profile solution in Visual Studio"),
            ["switch"] = ("-profile <ProfileName> switch", "Switches the active profile, may stash changes and check out Git branches, restores compiled packages, changes deployment links, and applies its database to web.config"),
            ["db-set"] = ("-profile <ProfileName> db-set <DatabaseName>", "Saves the database name, may update the external profile artifact, and applies it to web.config when the profile is active"),
            ["db-apply"] = ("-profile <ProfileName> db-apply", "Applies the profile database setting to web.config"),
            ["peri"] = ("-profile <ProfileName> -model <ModelName> peri <Task>", "Assigns and saves task metadata only; does not check out a branch"),
            ["solution-path"] = ("-profile <ProfileName> solution-path", "Shows the profile solution path"),
            ["solution-ensure"] = ("-profile <ProfileName> solution-ensure", "Creates or updates the profile solution"),
            ["package-build"] = ("-profile <ProfileName> -model <ModelName> package-build", "Builds a local package only; never pushes or publishes it"),
            ["repos"] = ("-profile <ProfileName> repos", "Lists repositories in the profile")
        };

        public string ProfileName { get; private set; }
        public string ModelName { get; private set; }
        public string Command { get; private set; }
        public string FilePath { get; private set; }
        public string DatabaseName { get; private set; }
        public bool IsValid { get; private set; }
        public bool IsHelpRequested { get; private set; }

        public CommandParser(string[] args)
        {
            ParseArguments(args);
        }

        public static CommandParser Parse(string[] args) => new CommandParser(args);

        private void ParseArguments(string[] args)
        {
            if (args == null)
            {
                MessageLogger.Error("Arguments are required");
                return;
            }

            bool helpRequested = args.Length == 0;
            bool profileSeen = false;
            bool modelSeen = false;
            var positional = new List<string>();

            for (int i = 0; i < args.Length; i++)
            {
                string token = args[i];
                if (string.IsNullOrWhiteSpace(token))
                {
                    MessageLogger.Error("Arguments cannot be empty");
                    return;
                }

                if (IsHelp(token))
                {
                    if (helpRequested)
                    {
                        MessageLogger.Error("Duplicate help argument");
                        return;
                    }
                    helpRequested = true;
                    continue;
                }

                bool isProfile = token.Equals("-profile", StringComparison.OrdinalIgnoreCase)
                    || token.Equals("--profile", StringComparison.OrdinalIgnoreCase);
                bool isModel = token.Equals("-model", StringComparison.OrdinalIgnoreCase)
                    || token.Equals("--model", StringComparison.OrdinalIgnoreCase);
                if (isProfile || isModel)
                {
                    if (isProfile ? profileSeen : modelSeen)
                    {
                        MessageLogger.Error("Duplicate profile or model option");
                        return;
                    }
                    if (isProfile) profileSeen = true;
                    else modelSeen = true;

                    if (i + 1 >= args.Length || string.IsNullOrWhiteSpace(args[i + 1])
                        || args[i + 1].StartsWith("-", StringComparison.Ordinal) || IsHelp(args[i + 1]))
                    {
                        MessageLogger.Error("Profile and model options require a value");
                        return;
                    }

                    string next = args[i + 1];
                    // Preserve legacy scope shorthand without treating every command as an omitted name
                    if (Command == null && ((isProfile && (next.Equals("list", StringComparison.OrdinalIgnoreCase)
                        || next.Equals("import", StringComparison.OrdinalIgnoreCase)))
                        || (isModel && next.Equals("add", StringComparison.OrdinalIgnoreCase))))
                    {
                        Command = next.ToLowerInvariant();
                        i++;
                        continue;
                    }

                    if (isProfile) ProfileName = next;
                    else ModelName = next;
                    i++;
                    continue;
                }

                if (token.StartsWith("-", StringComparison.Ordinal))
                {
                    MessageLogger.Error("Unknown option");
                    return;
                }

                if (Command == null)
                {
                    string command = token.Equals("git-check", StringComparison.OrdinalIgnoreCase)
                        ? "git-status" : token.ToLowerInvariant();
                    if (!Commands.ContainsKey(command))
                    {
                        MessageLogger.Error("Unknown command. Use 'fodev.exe help' to list commands");
                        return;
                    }
                    Command = command;
                }
                else
                {
                    positional.Add(token);
                }
            }

            if (Command == null)
            {
                if (helpRequested && !profileSeen && !modelSeen)
                {
                    IsHelpRequested = true;
                    ShowGeneralHelp();
                }
                else MessageLogger.Error("A command is required");
                return;
            }

            bool requiresProfile = Command != "list" && Command != "import";
            bool requiresModel = Command is "remove" or "git-status" or "git-open" or "peri" or "package-build";
            bool allowsModel = requiresModel || Command is "add" or "check" or "deploy" or "undeploy" or "show";
            bool requiresArgument = Command is "import" or "add" or "export" or "db-set" or "peri";

            if ((!allowsModel && modelSeen) || (Command == "import" && ProfileName != null)
                || positional.Count > (requiresArgument ? 1 : 0))
            {
                MessageLogger.Error("Unexpected options or extra arguments for this command");
                ShowCommandHelp(Command);
                return;
            }

            if (helpRequested)
            {
                IsHelpRequested = true;
                ShowCommandHelp(Command);
                return;
            }

            if ((requiresProfile && ProfileName == null) || (requiresModel && ModelName == null)
                || (requiresArgument && positional.Count != 1))
            {
                MessageLogger.Error("Missing required profile, model, or command argument");
                ShowCommandHelp(Command);
                return;
            }

            if (requiresArgument)
            {
                if (Command == "db-set") DatabaseName = positional[0];
                else FilePath = positional[0];
            }
            IsValid = true;
        }

        private static bool IsHelp(string value) => value != null &&
            (value.Equals("help", StringComparison.OrdinalIgnoreCase)
            || value.Equals("-h", StringComparison.OrdinalIgnoreCase)
            || value.Equals("--help", StringComparison.OrdinalIgnoreCase) || value == "?");

        private static void ShowGeneralHelp()
        {
            MessageLogger.Info("FODevManager - Dynamics 365 FO Developer Profile Manager\n"
                + "Usage: fodev.exe [-profile <ProfileName>] [-model <ModelName>] <command> [argument]\n"
                + "Options: -profile / --profile, -model / --model (case-insensitive; values are preserved)\n"
                + "Help: help, -h, --help, ?; use help <command> or <command> --help");
            foreach (var command in Commands)
                MessageLogger.Info($"  {command.Key}: {command.Value.Description}");
        }

        private static void ShowCommandHelp(string command)
        {
            var help = Commands[command];
            MessageLogger.Info($"Usage: fodev.exe {help.Usage}\n{help.Description}");
        }
    }
}
