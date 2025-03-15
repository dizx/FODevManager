using System;
using System.IO;
using System.Text;

namespace FODevManager.Services
{
    public class VisualStudioSolutionService
    {
        private readonly string _defaultSourceDirectory;

        public VisualStudioSolutionService(string defaultSourceDirectory)
        {
            _defaultSourceDirectory = defaultSourceDirectory;
        }

        public string GetSolutionFilePath(string profileName)
        {
            return Path.Combine(_defaultSourceDirectory, $"{profileName}.sln");
        }

        public void CreateSolutionFile(string profileName)
        {
            string solutionFilePath = GetSolutionFilePath(profileName);

            if (File.Exists(solutionFilePath))
            {
                Console.WriteLine($"Solution file already exists for profile '{profileName}'.");
                return;
            }

            var sb = new StringBuilder();
            sb.AppendLine("Microsoft Visual Studio Solution File, Format Version 12.00");
            sb.AppendLine("# Visual Studio Version 17");
            sb.AppendLine("VisualStudioVersion = 17.12.35527.113");

            File.WriteAllText(solutionFilePath, sb.ToString());
            Console.WriteLine($"Created solution file: {solutionFilePath}");
        }

        public void AddProjectToSolution(string profileName, string modelName, string projectFilePath)
        {
            string solutionFilePath = GetSolutionFilePath(profileName);

            if (!File.Exists(solutionFilePath))
            {
                Console.WriteLine($"Solution file does not exist. Creating one...");
                CreateSolutionFile(profileName);
            }

            string projectGuid = Guid.NewGuid().ToString("B").ToUpper();
            string relativePath = Path.GetRelativePath(_defaultSourceDirectory, projectFilePath);

            var sb = new StringBuilder(File.ReadAllText(solutionFilePath));
            sb.AppendLine($"Project(\"{projectGuid}\") = \"{modelName}\", \"{relativePath}\", \"{projectGuid}\"");
            sb.AppendLine("EndProject");

            File.WriteAllText(solutionFilePath, sb.ToString());
            Console.WriteLine($"Added project '{modelName}' to solution '{profileName}.sln'.");
        }

        public void RemoveProjectFromSolution(string profileName, string modelName)
        {
            string solutionFilePath = GetSolutionFilePath(profileName);

            if (!File.Exists(solutionFilePath))
            {
                Console.WriteLine($"Solution file for profile '{profileName}' does not exist.");
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
            Console.WriteLine($"Removed project '{modelName}' from solution '{profileName}.sln'.");
        }
    }
}
