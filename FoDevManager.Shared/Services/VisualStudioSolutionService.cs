using FODevManager.Messages;
using FODevManager.Models;
using FODevManager.Utils;
using Microsoft.Extensions.Configuration;
using System;
using System.Diagnostics;
using System.IO;
using System.Text;

namespace FODevManager.Services
{
    public class VisualStudioSolutionService
    {
        private readonly string _defaultSourceDirectory;

        public VisualStudioSolutionService(AppConfig config)
        {
            _defaultSourceDirectory = config.DefaultSourceDirectory;
        }

        public string GetSolutionFilePath(string profileName)
        {
            return Path.Combine(_defaultSourceDirectory, profileName, $"{profileName}.sln");
        }

        public string GetSolutionFilePath(ProfileModel profile)
        {
            if (profile == null) throw new ArgumentNullException(nameof(profile));

            if (!string.IsNullOrWhiteSpace(profile.SolutionFilePath))
                return Path.GetFullPath(profile.SolutionFilePath);

            var mainEnv = profile.Environments?.FirstOrDefault(e => e.IsMainFOModel);
            if (mainEnv != null && !string.IsNullOrWhiteSpace(mainEnv.ModelRootFolder))
                return Path.Combine(mainEnv.ModelRootFolder, $"{profile.ProfileName}.sln");

            return Path.Combine(_defaultSourceDirectory, profile.ProfileName, $"{profile.ProfileName}.sln");
        }

        public string GetSolutionDirectory(ProfileModel profile)
        {
            if (profile == null) throw new ArgumentNullException(nameof(profile));

            if (!string.IsNullOrWhiteSpace(profile.SolutionFilePath))
                return Path.GetDirectoryName(Path.GetFullPath(profile.SolutionFilePath))!;

            var mainEnv = profile.Environments?.FirstOrDefault(e => e.IsMainFOModel);
            if (mainEnv != null && !string.IsNullOrWhiteSpace(mainEnv.ModelRootFolder))
                return mainEnv.ModelRootFolder;

            return Path.Combine(_defaultSourceDirectory, profile.ProfileName);
        }


        public string GetSolutionDirectory(string profileName)
        {
            return Path.Combine(_defaultSourceDirectory, profileName);
        }

        public string CreateSolutionFile(string profileName)
        {
            string solutionDir = GetSolutionDirectory(profileName);
            string solutionFilePath = GetSolutionFilePath(profileName);

            return CreateSolutionFile(profileName, solutionDir, solutionFilePath);
        }

        public string CreateSolutionFile(ProfileModel profile)
        {
            string solutionDir = GetSolutionDirectory(profile);
            var solutionFilePath = GetSolutionFilePath(profile);
            
            return CreateSolutionFile(profile.ProfileName, solutionDir, solutionFilePath);
        }

        private string CreateSolutionFile(string profileName, string solutionDir, string solutionFilePath)
        {
            if (!Directory.Exists(solutionDir))
            {
                Directory.CreateDirectory(solutionDir);
            }

            if (File.Exists(solutionFilePath))
            {
                MessageLogger.Warning($"Solution file already exists for profile '{profileName}'.");
                return solutionFilePath;
            }

            var sb = new StringBuilder();
            sb.AppendLine("Microsoft Visual Studio Solution File, Format Version 12.00");
            sb.AppendLine("# Visual Studio Version 17");
            sb.AppendLine("VisualStudioVersion = 17.12.35527.113");

            File.WriteAllText(solutionFilePath, sb.ToString());
            MessageLogger.Info($"✅ Created solution file: {solutionFilePath}");

            return solutionFilePath;
        }

        public void AddProjectToSolution(ProfileModel profile, ProfileEnvironmentModel environment)
        {
            if (profile == null) throw new ArgumentNullException(nameof(profile));
            if (environment == null) throw new ArgumentNullException(nameof(environment));

            var projectFilePath = environment.ProjectFilePath;
            if (string.IsNullOrWhiteSpace(projectFilePath))
            {
                MessageLogger.Error("Environment has no ProjectFilePath.");
                return;
            }

            if (!File.Exists(projectFilePath))
            {
                MessageLogger.Error($"Project file not found: {projectFilePath}");
                return;
            }

            var solutionDir = GetSolutionDirectory(profile);
            var solutionFilePath = GetSolutionFilePath(profile);

            if (!File.Exists(solutionFilePath))
            {
                MessageLogger.Info($"Solution file does not exist for profile '{profile.ProfileName}'. Creating one...");
                CreateSolutionFile(profile); 
            }


            var lines = File.ReadAllLines(solutionFilePath);

            var alreadyInSolution = lines
                .Select(TryGetProjectNameFromLine)
                .Where(name => !name.IsNullOrEmpty())
                .Any(name => name!.Contains(environment.ModelName, StringComparison.OrdinalIgnoreCase));

            if (alreadyInSolution)
            {
                MessageLogger.Warning($"Project '{environment.ModelName}' already in solution.");
                return;
            }


            // .sln wants backslashes
            var relativePath = Path.GetRelativePath(solutionDir, projectFilePath)
                                   .Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar)
                                   .Replace('/', '\\');

         
            var projectTypeGuid = "{FC65038C-1B2F-41E1-A629-BED71D161FFF}";
            var projectGuid = Guid.NewGuid().ToString("B").ToUpper();

            var sb = new StringBuilder(File.ReadAllText(solutionFilePath));
            sb.AppendLine($"Project(\"{projectTypeGuid}\") = \"{environment.ModelName}\",    \"{relativePath}\", \"{projectGuid}\"");
            sb.AppendLine("EndProject");

            File.WriteAllText(solutionFilePath, sb.ToString());
            MessageLogger.Info($"Added project '{environment.ModelName}' to solution '{profile.ProfileName}.sln'.");
        }

        private static string? TryGetProjectNameFromLine(string line)
        {
            // Example line:
            // Project("{TYPE-GUID}") = "MyModelName", "MyModelName\MyModelName.rnrproj", "{PROJECT-GUID}"
            if (!line.TrimStart().StartsWith("Project(", StringComparison.OrdinalIgnoreCase))
                return null;

            var parts = line.Split('"');
            // indices:
            // 0: Project(
            // 1: TYPE-GUID
            // 2: ) = 
            // 3: PROJECT NAME
            // 4: , 
            // 5: PROJECT PATH
            if (parts.Length < 4)
                return null;

            return parts[3].Trim();
        }


        public void RemoveProjectFromSolution(string profileName, string modelName)
        {
            string solutionFilePath = GetSolutionFilePath(profileName);

            if (!File.Exists(solutionFilePath))
            {
                MessageLogger.Warning($"Solution file for profile '{profileName}' does not exist.");
                return;
            }

            string[] lines = File.ReadAllLines(solutionFilePath);
            var sb = new StringBuilder();

            bool insideProjectBlock = false;

            foreach (var line in lines)
            {
                if (line.Contains($"= \"{modelName}"))
                {
                    insideProjectBlock = true;
                    continue;
                }

                if (insideProjectBlock && line.Contains("EndProject"))
                {
                    insideProjectBlock = false;
                    continue;
                }

                if (!insideProjectBlock)
                {
                    sb.AppendLine(line);
                }
            }

            File.WriteAllText(solutionFilePath, sb.ToString());
            MessageLogger.Info($"Removed project '{modelName}' from solution '{profileName}.sln'.");
        }

        public void OpenSolution(string solutionPath)
        {
            if (string.IsNullOrWhiteSpace(solutionPath) || !File.Exists(solutionPath))
            {
                MessageLogger.Error("Solution file not found.");
                return;
            }

            try
            {
                // Use Process.Start to open the solution in Visual Studio
                Process.Start(new ProcessStartInfo(solutionPath)
                {
                    UseShellExecute = true
                });

                MessageLogger.Info($"Opening solution: {solutionPath}");
            }
            catch (Exception ex)
            {
                MessageLogger.Error($"Failed to open solution: {ex.Message}");
            }
        }
    }
}
