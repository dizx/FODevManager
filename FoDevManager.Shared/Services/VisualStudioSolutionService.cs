using FODevManager.Utils;
using Microsoft.Extensions.Configuration;
using System;
using System.IO;
using System.Text;
using FoDevManager.Messages;
using System.Diagnostics;

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

        public string GetSolutionDirectory(string profileName)
        {
            return Path.Combine(_defaultSourceDirectory, profileName);
        }

        public string CreateSolutionFile(string profileName)
        {
            string solutionDir = GetSolutionDirectory(profileName);
            string solutionFilePath = GetSolutionFilePath(profileName);

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

        public void AddProjectToSolution(string profileName, string modelName, string projectFilePath)
        {
            string solutionDir = GetSolutionDirectory(profileName);
            string solutionFilePath = GetSolutionFilePath(profileName);

            if (!File.Exists(solutionFilePath))
            {
                MessageLogger.Info($"Solution file does not exist for profile '{profileName}'. Creating one...");
                CreateSolutionFile(profileName);
            }

            string projectGuid = Guid.NewGuid().ToString("B").ToUpper();
            string relativePath = Path.GetRelativePath(solutionDir, projectFilePath);

            var sb = new StringBuilder(File.ReadAllText(solutionFilePath));
            sb.AppendLine($"Project(\"{projectGuid}\") = \"{modelName}\", \"{relativePath}\", \"{projectGuid}\"");
            sb.AppendLine("EndProject");

            File.WriteAllText(solutionFilePath, sb.ToString());
            MessageLogger.Info($"✅ Added project '{modelName}' to solution '{profileName}.sln'.");
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
                if (line.Contains($"= \"{modelName}\""))
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
