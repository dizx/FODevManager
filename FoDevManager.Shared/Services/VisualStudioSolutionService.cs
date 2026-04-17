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

        public string GetSolutionFilePath(ProfileModel profile)
        {
            if (profile == null) throw new ArgumentNullException(nameof(profile));

            if (!profile.SolutionFilePath.IsNullOrEmpty())
                return Path.GetFullPath(profile.SolutionFilePath);

            var mainEnv = profile.AllModels.FirstOrDefault(e => e.IsMainFOModel);
            if (mainEnv != null && !mainEnv.ModelRootFolder.IsNullOrEmpty())
                return Path.Combine(mainEnv.ModelRootFolder, $"{profile.ProfileName}.sln");

            return Path.Combine(_defaultSourceDirectory, profile.ProfileName, $"{profile.ProfileName}.sln");
        }

        public string GetSolutionDirectory(ProfileModel profile)
        {
            if (profile == null) throw new ArgumentNullException(nameof(profile));

            if (!profile.SolutionFilePath.IsNullOrEmpty())
                return Path.GetDirectoryName(Path.GetFullPath(profile.SolutionFilePath))!;

            var mainEnv = profile.AllModels?.FirstOrDefault(e => e.IsMainFOModel);
            if (mainEnv != null && !mainEnv.ModelRootFolder.IsNullOrEmpty())
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
            string solutionFilePath = GetSolutionFilePath(new ProfileModel { ProfileName = profileName });

            return CreateSolutionFile(profileName, solutionDir, solutionFilePath);
        }

        public string CreateSolutionFile(ProfileModel profile)
        {
            string solutionDir = GetSolutionDirectory(profile);
            var solutionFilePath = GetSolutionFilePath(profile);
            
            return CreateSolutionFile(profile.ProfileName, solutionDir, solutionFilePath);
        }

        public string CreateBuildSolution(ProfileModel profile, ProfileEnvironmentModel model)
        {
            if (profile == null) throw new ArgumentNullException(nameof(profile));
            if (model == null) throw new ArgumentNullException(nameof(model));
            if (model.ProjectFilePath.IsNullOrEmpty())
                throw new ArgumentException("Model project file path is required", nameof(model));

            var projectFilePath = Path.GetFullPath(model.ProjectFilePath);
            var solutionDirectory = Path.GetDirectoryName(projectFilePath);
            if (solutionDirectory.IsNullOrEmpty())
                throw new InvalidOperationException($"Could not resolve a solution directory for '{projectFilePath}'");

            if (!File.Exists(projectFilePath))
                throw new FileNotFoundException("Source project file not found", projectFilePath);

            var sourceSolutionFilePath = GetSolutionFilePath(profile);
            if (!File.Exists(sourceSolutionFilePath))
                throw new FileNotFoundException("Source solution file not found", sourceSolutionFilePath);

            Directory.CreateDirectory(solutionDirectory);
            var solutionFilePath = Path.Combine(solutionDirectory, $"{model.ModelName}.packagebuild.sln");
            var relativeProjectPath = Path.GetRelativePath(solutionDirectory, projectFilePath)
                .Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar)
                .Replace('/', '\\');

            var lines = File.ReadAllLines(sourceSolutionFilePath);
            var builder = new StringBuilder();
            var keepProjectBlock = false;
            var projectFound = false;

            foreach (var line in lines)
            {
                var trimmedLine = line.TrimStart();
                if (trimmedLine.StartsWith("Project(", StringComparison.Ordinal))
                {
                    var projectName = TryGetProjectNameFromLine(line);
                    keepProjectBlock = !projectName.IsNullOrEmpty() && projectName!.Contains(model.ModelName, StringComparison.OrdinalIgnoreCase);
                    if (keepProjectBlock)
                    {
                        projectFound = true;
                        var parts = line.Split('"');
                        if (parts.Length >= 6)
                        {
                            builder.AppendLine($"{parts[0]}\"{parts[1]}\"{parts[2]}\"{parts[3]}\"{parts[4]}\"{relativeProjectPath}\"{string.Join("\"", parts.Skip(6))}");
                            continue;
                        }
                    }
                }

                if (keepProjectBlock || !trimmedLine.StartsWith("Project(", StringComparison.Ordinal))
                    builder.AppendLine(line);

                if (trimmedLine.Equals("EndProject", StringComparison.Ordinal))
                    keepProjectBlock = false;
            }

            if (!projectFound)
                throw new InvalidOperationException($"Model '{model.ModelName}' could not be found in solution '{sourceSolutionFilePath}'");

            File.WriteAllText(solutionFilePath, builder.ToString());
            
            return solutionFilePath;
        }

        private string CreateSolutionFile(string profileName, string solutionDir, string solutionFilePath)
        {
            if (!Directory.Exists(solutionDir))
            {
                Directory.CreateDirectory(solutionDir);
            }

            if (File.Exists(solutionFilePath))
            {
                MessageLogger.Warning($"Solution file already exists for profile '{profileName}'");
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

        public void AddProjectToSolution(ProfileModel profile, ProfileEnvironmentModel model)
        {
            if (profile == null) throw new ArgumentNullException(nameof(profile));
            if (model == null) throw new ArgumentNullException(nameof(model));

            var projectFilePath = model.ProjectFilePath;
            if (projectFilePath.IsNullOrEmpty())
            {
                MessageLogger.Error("Environment has no ProjectFilePath");
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
                MessageLogger.Info($"Solution file does not exist for profile '{profile.ProfileName}'. Creating one..");
                CreateSolutionFile(profile); 
            }


            var lines = File.ReadAllLines(solutionFilePath);

            var alreadyInSolution = lines
                .Select(TryGetProjectNameFromLine)
                .Where(name => !name.IsNullOrEmpty())
                .Any(name => name!.Contains(model.ModelName));

            if (alreadyInSolution)
            {
                MessageLogger.Warning($"Project '{model.ModelName}' already in solution");
                return;
            }


            // .sln wants backslashes
            var relativePath = Path.GetRelativePath(solutionDir, projectFilePath)
                                   .Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar)
                                   .Replace('/', '\\');

         
            var projectTypeGuid = "{FC65038C-1B2F-41E1-A629-BED71D161FFF}";
            var projectGuid = Guid.NewGuid().ToString("B").ToUpper();

            var sb = new StringBuilder(File.ReadAllText(solutionFilePath));
            sb.AppendLine($"Project(\"{projectTypeGuid}\") = \"{model.ModelName}\",    \"{relativePath}\", \"{projectGuid}\"");
            sb.AppendLine("EndProject");

            File.WriteAllText(solutionFilePath, sb.ToString());
            MessageLogger.Info($"Added project '{model.ModelName}' to solution '{profile.ProfileName}.sln'");
        }

        private static string? TryGetProjectNameFromLine(string line)
        {
            // Example line:
            // Project("{TYPE-GUID}") = "MyModelName", "MyModelName\MyModelName.rnrproj", "{PROJECT-GUID}"
            if (!line.TrimStart().StartsWith("Project("))
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


        public void RemoveProjectFromSolution(ProfileModel profile, string modelName)
        {
            string solutionFilePath = GetSolutionFilePath(profile);

            if (!File.Exists(solutionFilePath))
            {
                MessageLogger.Warning($"Solution file for profile '{profile.ProfileName}' does not exist");
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
            MessageLogger.Info($"Removed project '{modelName}' from solution '{profile.ProfileName}.sln'");
        }

        public void OpenSolution(string solutionPath)
        {
            if (solutionPath.IsNullOrEmpty() || !File.Exists(solutionPath))
            {
                MessageLogger.Error("Solution file not found");
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
