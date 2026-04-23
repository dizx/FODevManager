using FODevManager.Messages;
using FODevManager.Models;
using FODevManager.Utils;
using Microsoft.Extensions.Configuration;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Xml.Linq;

namespace FODevManager.Services
{
    public class VisualStudioSolutionService
    {
        private const string CSharpProjectTypeGuid = "{FAE04EC0-301F-11D3-BF4B-00C04F79EFBC}";
        private const string SolutionFolderProjectTypeGuid = "{2150E333-8FDC-42A3-9474-1A3956D46DE8}";
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
            var sourceSolutionDirectory = Path.GetDirectoryName(sourceSolutionFilePath) ?? solutionDirectory;
            var projectReferences = LoadProjectReferences(projectFilePath);
            var sourceProjects = ParseSolutionProjects(lines);
            var nestedProjects = ParseNestedProjects(lines);

            var projectFound = sourceProjects.Any(project => IsMatchingProject(project, sourceSolutionDirectory, projectFilePath, model.ModelName));

            if (!projectFound)
                throw new InvalidOperationException($"Model '{model.ModelName}' could not be found in solution '{sourceSolutionFilePath}'");

            var updatedSourceLines = UpdateSolutionProjectReferenceGuids(
                lines,
                sourceProjects,
                sourceSolutionDirectory,
                projectReferences);

            if (!ReferenceEquals(lines, updatedSourceLines))
            {
                File.WriteAllLines(sourceSolutionFilePath, updatedSourceLines);
                lines = updatedSourceLines;
                sourceProjects = ParseSolutionProjects(lines);
                nestedProjects = ParseNestedProjects(lines);
                MessageLogger.Info($"Updated project GUIDs in source solution '{sourceSolutionFilePath}' to match project references");
            }

            var includedProjects = ResolveBuildSolutionProjects(
                sourceProjects,
                sourceSolutionDirectory,
                projectFilePath,
                model.ModelName,
                projectReferences,
                nestedProjects);

            var builder = BuildFilteredSolution(
                lines,
                sourceProjects,
                includedProjects,
                nestedProjects,
                sourceSolutionDirectory,
                solutionDirectory,
                projectFilePath,
                relativeProjectPath);

            File.WriteAllText(solutionFilePath, builder.ToString());
            
            return solutionFilePath;
        }

        private static string[] UpdateSolutionProjectReferenceGuids(
            string[] lines,
            IReadOnlyCollection<SolutionProject> projects,
            string solutionDirectory,
            IReadOnlyCollection<ProjectReferenceInfo> projectReferences)
        {
            if (projectReferences.Count == 0)
                return lines;

            var updatedLines = (string[])lines.Clone();
            var updated = false;

            foreach (var projectReference in projectReferences.Where(reference => !reference.ProjectGuid.IsNullOrEmpty()))
            {
                var matchingProject = projects.FirstOrDefault(project =>
                    ProjectPathMatches(project, solutionDirectory, projectReference.FullPath)
                    && project.Name.SameAs(projectReference.Name));

                if (matchingProject == null || matchingProject.ProjectGuid.SameAs(projectReference.ProjectGuid))
                    continue;

                updatedLines[matchingProject.StartLineIndex] = ReplaceSolutionProjectGuid(
                    updatedLines[matchingProject.StartLineIndex],
                    matchingProject.ProjectGuid,
                    projectReference.ProjectGuid);

                for (var i = 0; i < updatedLines.Length; i++)
                {
                    if (i == matchingProject.StartLineIndex)
                        continue;

                    updatedLines[i] = ReplaceGuidText(updatedLines[i], matchingProject.ProjectGuid, projectReference.ProjectGuid);
                }

                updated = true;
            }

            return updated ? updatedLines : lines;
        }

        private static IReadOnlyCollection<SolutionProject> ResolveBuildSolutionProjects(
            IReadOnlyCollection<SolutionProject> sourceProjects,
            string sourceSolutionDirectory,
            string modelProjectFilePath,
            string modelName,
            IReadOnlyCollection<ProjectReferenceInfo> projectReferences,
            IReadOnlyCollection<NestedProject> nestedProjects)
        {
            var includedProjects = new Dictionary<string, SolutionProject>(StringComparer.OrdinalIgnoreCase);

            foreach (var project in sourceProjects.Where(project => IsMatchingProject(project, sourceSolutionDirectory, modelProjectFilePath, modelName)))
            {
                includedProjects[project.ProjectGuid] = project;
            }

            foreach (var projectReference in projectReferences)
            {
                var referencedProject = sourceProjects.FirstOrDefault(project =>
                        ProjectPathMatches(project, sourceSolutionDirectory, projectReference.FullPath)
                        && project.Name.SameAs(projectReference.Name))
                    ?? sourceProjects.FirstOrDefault(project =>
                        ProjectPathMatches(project, sourceSolutionDirectory, projectReference.FullPath));

                if (referencedProject != null)
                {
                    includedProjects[referencedProject.ProjectGuid] = referencedProject;
                    continue;
                }

                if (!projectReference.ProjectGuid.IsNullOrEmpty() && File.Exists(projectReference.FullPath))
                {
                    includedProjects[projectReference.ProjectGuid] = new SolutionProject(
                        CSharpProjectTypeGuid,
                        projectReference.Name,
                        projectReference.FullPath,
                        projectReference.ProjectGuid,
                        -1,
                        -1,
                        Array.Empty<string>());
                }
            }

            IncludeNestedSolutionFolders(sourceProjects, nestedProjects, includedProjects);

            return includedProjects.Values
                .OrderBy(project => project.StartLineIndex < 0 ? int.MaxValue : project.StartLineIndex)
                .ThenBy(project => project.Name, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        private static void IncludeNestedSolutionFolders(
            IReadOnlyCollection<SolutionProject> sourceProjects,
            IReadOnlyCollection<NestedProject> nestedProjects,
            Dictionary<string, SolutionProject> includedProjects)
        {
            var projectsByGuid = sourceProjects.ToDictionary(project => project.ProjectGuid, StringComparer.OrdinalIgnoreCase);
            var parentsByChildGuid = nestedProjects
                .GroupBy(nestedProject => nestedProject.ChildGuid, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(group => group.Key, group => group.First().ParentGuid, StringComparer.OrdinalIgnoreCase);

            var pendingChildren = includedProjects.Keys.ToList();

            foreach (var childGuid in pendingChildren)
            {
                var currentGuid = childGuid;
                var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

                while (parentsByChildGuid.TryGetValue(currentGuid, out var parentGuid) && visited.Add(parentGuid))
                {
                    if (projectsByGuid.TryGetValue(parentGuid, out var parentProject)
                        && parentProject.ProjectTypeGuid.SameAs(SolutionFolderProjectTypeGuid))
                    {
                        includedProjects[parentProject.ProjectGuid] = parentProject;
                    }

                    currentGuid = parentGuid;
                }
            }
        }

        private static StringBuilder BuildFilteredSolution(
            IReadOnlyList<string> lines,
            IReadOnlyCollection<SolutionProject> sourceProjects,
            IReadOnlyCollection<SolutionProject> includedProjects,
            IReadOnlyCollection<NestedProject> nestedProjects,
            string sourceSolutionDirectory,
            string buildSolutionDirectory,
            string modelProjectFilePath,
            string modelRelativeProjectPath)
        {
            var includedProjectGuids = includedProjects
                .Select(project => project.ProjectGuid)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            var includedProjectsByGuid = includedProjects.ToDictionary(project => project.ProjectGuid, StringComparer.OrdinalIgnoreCase);
            var sourceProjectsByStartLine = sourceProjects.ToDictionary(project => project.StartLineIndex);
            var builder = new StringBuilder();
            var emittedAddedProjects = false;

            for (var i = 0; i < lines.Count; i++)
            {
                if (sourceProjectsByStartLine.TryGetValue(i, out var sourceProject))
                {
                    if (includedProjectsByGuid.TryGetValue(sourceProject.ProjectGuid, out var includedProject))
                        AppendProjectBlock(builder, includedProject, sourceSolutionDirectory, buildSolutionDirectory, modelProjectFilePath, modelRelativeProjectPath, includedProjectGuids);

                    i = sourceProject.EndLineIndex;
                    continue;
                }

                var trimmedLine = lines[i].TrimStart();
                if (trimmedLine.StartsWith("GlobalSection(", StringComparison.Ordinal))
                {
                    var sectionLines = ReadGlobalSection(lines, ref i);
                    AppendFilteredGlobalSection(builder, sectionLines, includedProjectGuids, nestedProjects);
                    continue;
                }

                if (!emittedAddedProjects && trimmedLine.StartsWith("Global", StringComparison.Ordinal))
                {
                    foreach (var addedProject in includedProjects.Where(project => project.StartLineIndex < 0))
                    {
                        AppendProjectBlock(builder, addedProject, sourceSolutionDirectory, buildSolutionDirectory, modelProjectFilePath, modelRelativeProjectPath, includedProjectGuids);
                    }

                    emittedAddedProjects = true;
                }

                builder.AppendLine(lines[i]);
            }

            if (!emittedAddedProjects)
            {
                foreach (var addedProject in includedProjects.Where(project => project.StartLineIndex < 0))
                {
                    AppendProjectBlock(builder, addedProject, sourceSolutionDirectory, buildSolutionDirectory, modelProjectFilePath, modelRelativeProjectPath, includedProjectGuids);
                }
            }

            return builder;
        }

        private static void AppendProjectBlock(
            StringBuilder builder,
            SolutionProject project,
            string sourceSolutionDirectory,
            string buildSolutionDirectory,
            string modelProjectFilePath,
            string modelRelativeProjectPath,
            HashSet<string> includedProjectGuids)
        {
            var projectPath = ProjectPathMatches(project, sourceSolutionDirectory, modelProjectFilePath)
                ? modelRelativeProjectPath
                : GetBuildSolutionRelativePath(project, sourceSolutionDirectory, buildSolutionDirectory);

            builder.AppendLine($"Project(\"{project.ProjectTypeGuid}\") = \"{project.Name}\", \"{projectPath}\", \"{project.ProjectGuid}\"");

            foreach (var line in FilterProjectInnerLines(project.InnerLines, includedProjectGuids))
            {
                builder.AppendLine(line);
            }

            builder.AppendLine("EndProject");
        }

        private static IEnumerable<string> FilterProjectInnerLines(IReadOnlyList<string> innerLines, HashSet<string> includedProjectGuids)
        {
            for (var i = 0; i < innerLines.Count; i++)
            {
                var trimmedLine = innerLines[i].TrimStart();
                if (!trimmedLine.StartsWith("ProjectSection(ProjectDependencies)", StringComparison.Ordinal))
                {
                    yield return innerLines[i];
                    continue;
                }

                var sectionLines = new List<string> { innerLines[i] };
                for (i++; i < innerLines.Count; i++)
                {
                    sectionLines.Add(innerLines[i]);
                    if (innerLines[i].TrimStart().StartsWith("EndProjectSection", StringComparison.Ordinal))
                        break;
                }

                var dependencyLines = sectionLines
                    .Skip(1)
                    .Take(sectionLines.Count - 2)
                    .Where(line => TryGetLeadingGuid(line, out var guid) && includedProjectGuids.Contains(guid))
                    .ToList();

                if (dependencyLines.Count == 0)
                    continue;

                yield return sectionLines[0];
                foreach (var dependencyLine in dependencyLines)
                    yield return dependencyLine;
                yield return sectionLines[^1];
            }
        }

        private static string GetBuildSolutionRelativePath(SolutionProject project, string sourceSolutionDirectory, string buildSolutionDirectory)
        {
            if (project.ProjectTypeGuid.SameAs(SolutionFolderProjectTypeGuid))
                return project.RelativePath;

            var fullPath = Path.IsPathRooted(project.RelativePath)
                ? project.RelativePath
                : Path.GetFullPath(Path.Combine(sourceSolutionDirectory, project.RelativePath));

            return Path.GetRelativePath(buildSolutionDirectory, fullPath)
                .Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar)
                .Replace('/', '\\');
        }

        private static List<string> ReadGlobalSection(IReadOnlyList<string> lines, ref int lineIndex)
        {
            var sectionLines = new List<string>();

            for (; lineIndex < lines.Count; lineIndex++)
            {
                sectionLines.Add(lines[lineIndex]);
                if (lines[lineIndex].TrimStart().StartsWith("EndGlobalSection", StringComparison.Ordinal))
                    break;
            }

            return sectionLines;
        }

        private static void AppendFilteredGlobalSection(
            StringBuilder builder,
            IReadOnlyList<string> sectionLines,
            HashSet<string> includedProjectGuids,
            IReadOnlyCollection<NestedProject> nestedProjects)
        {
            if (sectionLines.Count == 0)
                return;

            var sectionName = TryGetGlobalSectionName(sectionLines[0]);
            if (sectionName.SameAs("NestedProjects"))
            {
                var validNestedProjects = nestedProjects
                    .Where(nestedProject =>
                        includedProjectGuids.Contains(nestedProject.ChildGuid)
                        && includedProjectGuids.Contains(nestedProject.ParentGuid))
                    .ToList();

                if (validNestedProjects.Count == 0)
                    return;

                builder.AppendLine(sectionLines[0]);
                foreach (var nestedProject in validNestedProjects)
                    builder.AppendLine($"\t\t{nestedProject.ChildGuid} = {nestedProject.ParentGuid}");
                builder.AppendLine(sectionLines[^1]);
                return;
            }

            if (sectionName.SameAs("ProjectConfigurationPlatforms"))
            {
                var filteredLines = sectionLines
                    .Skip(1)
                    .Take(sectionLines.Count - 2)
                    .Where(line => TryGetLeadingGuid(line, out var guid) && includedProjectGuids.Contains(guid))
                    .ToList();

                if (filteredLines.Count == 0)
                    return;

                builder.AppendLine(sectionLines[0]);
                foreach (var line in filteredLines)
                    builder.AppendLine(line);
                builder.AppendLine(sectionLines[^1]);
                return;
            }

            foreach (var line in sectionLines)
                builder.AppendLine(line);
        }

        private static string? TryGetGlobalSectionName(string line)
        {
            var start = line.IndexOf('(', StringComparison.Ordinal);
            var end = line.IndexOf(')', StringComparison.Ordinal);
            if (start < 0 || end <= start)
                return null;

            return line[(start + 1)..end];
        }

        private static bool TryGetLeadingGuid(string line, out string guid)
        {
            guid = string.Empty;
            var trimmedLine = line.TrimStart();
            if (!trimmedLine.StartsWith("{", StringComparison.Ordinal))
                return false;

            var end = trimmedLine.IndexOf('}', StringComparison.Ordinal);
            if (end < 0)
                return false;

            guid = trimmedLine[..(end + 1)];
            return true;
        }

        private static IReadOnlyCollection<ProjectReferenceInfo> LoadProjectReferences(string projectFilePath)
        {
            try
            {
                var projectDirectory = Path.GetDirectoryName(projectFilePath) ?? Directory.GetCurrentDirectory();
                var document = XDocument.Load(projectFilePath);

                return document
                    .Descendants()
                    .Where(element => element.Name.LocalName == "ProjectReference")
                    .Select(element => CreateProjectReference(projectDirectory, element))
                    .Where(reference => reference != null)
                    .Cast<ProjectReferenceInfo>()
                    .ToList();
            }
            catch (Exception exception)
            {
                MessageLogger.Warning($"Could not read project references from '{projectFilePath}': {exception.Message}");
                return Array.Empty<ProjectReferenceInfo>();
            }
        }

        private static ProjectReferenceInfo? CreateProjectReference(string projectDirectory, XElement element)
        {
            var include = element.Attribute("Include")?.Value;
            if (include.IsNullOrEmpty())
                return null;

            var fullPath = Path.GetFullPath(Path.Combine(projectDirectory, include));
            var name = element.Elements()
                .FirstOrDefault(child => child.Name.LocalName == "Name")
                ?.Value;

            if (name.IsNullOrEmpty())
                name = Path.GetFileNameWithoutExtension(fullPath);

            var projectGuid = element.Elements()
                .FirstOrDefault(child => child.Name.LocalName == "Project")
                ?.Value;

            projectGuid = NormalizeGuid(projectGuid);

            return new ProjectReferenceInfo(name!, fullPath, projectGuid);
        }

        private static IReadOnlyCollection<SolutionProject> ParseSolutionProjects(IReadOnlyList<string> lines)
        {
            var projects = new List<SolutionProject>();

            for (var i = 0; i < lines.Count; i++)
            {
                if (!TryParseProjectLine(lines[i], out var projectTypeGuid, out var name, out var relativePath, out var projectGuid))
                    continue;

                var innerLines = new List<string>();
                var endLineIndex = i;

                for (var j = i + 1; j < lines.Count; j++)
                {
                    endLineIndex = j;
                    if (lines[j].TrimStart().Equals("EndProject", StringComparison.Ordinal))
                        break;

                    innerLines.Add(lines[j]);
                }

                projects.Add(new SolutionProject(projectTypeGuid, name, relativePath, projectGuid, i, endLineIndex, innerLines));
                i = endLineIndex;
            }

            return projects;
        }

        private static IReadOnlyCollection<NestedProject> ParseNestedProjects(IReadOnlyList<string> lines)
        {
            var nestedProjects = new List<NestedProject>();
            var inNestedProjects = false;

            foreach (var line in lines)
            {
                var trimmedLine = line.Trim();
                if (trimmedLine.StartsWith("GlobalSection(NestedProjects)", StringComparison.Ordinal))
                {
                    inNestedProjects = true;
                    continue;
                }

                if (inNestedProjects && trimmedLine.StartsWith("EndGlobalSection", StringComparison.Ordinal))
                {
                    inNestedProjects = false;
                    continue;
                }

                if (!inNestedProjects)
                    continue;

                var parts = trimmedLine.Split('=', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length == 2)
                    nestedProjects.Add(new NestedProject(NormalizeGuid(parts[0]), NormalizeGuid(parts[1])));
            }

            return nestedProjects;
        }

        private static bool TryParseProjectLine(
            string line,
            out string projectTypeGuid,
            out string name,
            out string relativePath,
            out string projectGuid)
        {
            projectTypeGuid = string.Empty;
            name = string.Empty;
            relativePath = string.Empty;
            projectGuid = string.Empty;

            if (!line.TrimStart().StartsWith("Project(", StringComparison.Ordinal))
                return false;

            var parts = line.Split('"');
            if (parts.Length < 8)
                return false;

            projectTypeGuid = NormalizeGuid(parts[1]);
            name = parts[3].Trim();
            relativePath = parts[5].Trim();
            projectGuid = NormalizeGuid(parts[7]);

            return !projectTypeGuid.IsNullOrEmpty()
                && !name.IsNullOrEmpty()
                && !relativePath.IsNullOrEmpty()
                && !projectGuid.IsNullOrEmpty();
        }

        private static bool IsMatchingProject(SolutionProject project, string solutionDirectory, string projectFilePath, string modelName)
        {
            return ProjectPathMatches(project, solutionDirectory, projectFilePath)
                || (!project.Name.IsNullOrEmpty() && project.Name.Contains(modelName, StringComparison.OrdinalIgnoreCase));
        }

        private static bool ProjectPathMatches(SolutionProject project, string solutionDirectory, string projectFilePath)
        {
            if (project.ProjectTypeGuid.SameAs(SolutionFolderProjectTypeGuid))
                return false;

            var fullProjectPath = Path.GetFullPath(Path.Combine(solutionDirectory, project.RelativePath));
            return AreSameFilePath(fullProjectPath, projectFilePath);
        }

        private static bool AreSameFilePath(string left, string right)
        {
            if (left.IsNullOrEmpty() || right.IsNullOrEmpty())
                return false;

            return Path.GetFullPath(left).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                .Equals(
                    Path.GetFullPath(right).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
                    StringComparison.OrdinalIgnoreCase);
        }

        private static string ReplaceSolutionProjectGuid(string line, string oldGuid, string newGuid)
        {
            var parts = line.Split('"');
            if (parts.Length < 8)
                return ReplaceGuidText(line, oldGuid, newGuid);

            parts[7] = NormalizeGuid(newGuid);
            return string.Join("\"", parts);
        }

        private static string ReplaceGuidText(string line, string oldGuid, string newGuid)
        {
            return line.Replace(NormalizeGuid(oldGuid), NormalizeGuid(newGuid), StringComparison.OrdinalIgnoreCase);
        }

        private static string NormalizeGuid(string? value)
        {
            if (value.IsNullOrEmpty())
                return string.Empty;

            var trimmedValue = value.Trim();
            if (!trimmedValue.StartsWith("{", StringComparison.Ordinal))
                trimmedValue = "{" + trimmedValue;

            if (!trimmedValue.EndsWith("}", StringComparison.Ordinal))
                trimmedValue += "}";

            return trimmedValue.ToUpperInvariant();
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

        private sealed record ProjectReferenceInfo(string Name, string FullPath, string ProjectGuid);

        private sealed record SolutionProject(
            string ProjectTypeGuid,
            string Name,
            string RelativePath,
            string ProjectGuid,
            int StartLineIndex,
            int EndLineIndex,
            IReadOnlyList<string> InnerLines);

        private sealed record NestedProject(string ChildGuid, string ParentGuid);
    }
}
