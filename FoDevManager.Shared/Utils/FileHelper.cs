using System;
using System.IO;
using System.Text.Json;

namespace FODevManager.Utils
{
    public static class FileHelper
    {
        public static void EnsureDirectoryExists(string path)
        {
            if (!Directory.Exists(path))
            {
                Directory.CreateDirectory(path);
            }
        }

        public static T LoadJson<T>(string filePath) where T : new()
        {
            if (!File.Exists(filePath))
            {
                return new T();
            }

            string json = File.ReadAllText(filePath);
            return JsonSerializer.Deserialize<T>(json) ?? new T();
        }

        public static void SaveJson<T>(string filePath, T data)
        {
            string json = JsonSerializer.Serialize(data, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(filePath, json);
        }

        public static bool PathContainsDirectory(string path, string directoryName)
        {
            string fullPath = Path.GetFullPath(path); // Normalize path
            string normalizedDir = Path.DirectorySeparatorChar + directoryName + Path.DirectorySeparatorChar;

            return fullPath.Contains(normalizedDir, StringComparison.OrdinalIgnoreCase);
        }


        public static string GetProjectFilePath(string profileName, string modelName, string projectFilePath)
        {
            //if (string.IsNullOrEmpty(projectFilePath))
            //{
            //    return Path.Combine(_defaultSourceDirectory, modelName, $"{modelName}.rnrproj");
            //}

            if (!Path.IsPathRooted(projectFilePath))
            {
                return Path.Combine(projectFilePath, modelName, $"{modelName}.rnrproj");
            }

            if (!Path.HasExtension(projectFilePath))
            {
                if (PathContainsDirectory(projectFilePath, "project") && PathContainsDirectory(projectFilePath, modelName))
                {
                    return projectFilePath = Path.Combine(projectFilePath, $"{modelName}.rnrproj");
                }
                if (PathContainsDirectory(projectFilePath, "project") && !PathContainsDirectory(projectFilePath, modelName))
                {
                    if (File.Exists(Path.Combine(projectFilePath, modelName, $"{modelName}.rnrproj")))
                        return projectFilePath = Path.Combine(projectFilePath, modelName, $"{modelName}.rnrproj");
                }
                if (File.Exists(Path.Combine(projectFilePath, modelName, $"{modelName}.rnrproj")))
                    return projectFilePath = Path.Combine(projectFilePath, modelName, $"{modelName}.rnrproj");

                if (File.Exists(Path.Combine(projectFilePath, "project", modelName, $"{modelName}.rnrproj")))
                    return Path.Combine(projectFilePath, "project", modelName, $"{modelName}.rnrproj");

            }
            return Path.Combine(projectFilePath, "project", modelName, modelName, $"{modelName}.rnrproj");
        }

        public static string GetMetadataFolder(string modelName, string projectFilePath)
        {
            string currentPath = Path.GetDirectoryName(projectFilePath);

            // Move up to 3 levels and check for Metadata folder
            for (int i = 0; i < 3; i++)
            {
                if (string.IsNullOrEmpty(currentPath)) break;

                string metadataPath = Path.Combine(currentPath, "Metadata", modelName);
                if (Directory.Exists(metadataPath))
                {
                    return metadataPath;
                }

                metadataPath = Path.Combine(currentPath, "Metadata");
                if (Directory.Exists(metadataPath))
                {
                    return Path.Combine(metadataPath, modelName);
                }

                // Move one level up
                currentPath = Directory.GetParent(currentPath)?.FullName;
            }

            // Default: return the highest-level Metadata folder if not found
            return Path.Combine(currentPath ?? projectFilePath, "Metadata", modelName);
        }

        public static string GetModelRootFolder(string projectFilePath)
        {
            string currentPath = Path.GetDirectoryName(projectFilePath);

            // Move up to 4 levels and check for Metadata folder
            for (int i = 0; i < 4; i++)
            {
                if (string.IsNullOrEmpty(currentPath)) break;

                string metadataPath = Path.Combine(currentPath, "Project");
                if (Directory.Exists(metadataPath))
                {
                    return currentPath;
                }

                // Move one level up
                currentPath = Directory.GetParent(currentPath)?.FullName;
            }

            throw new Exception($"Can't find model root of project {projectFilePath}");
        }


        static string GetParentDirectory(string path)
        {
            DirectoryInfo parentDir = Directory.GetParent(Path.GetDirectoryName(path));
            return parentDir?.FullName + Path.DirectorySeparatorChar;
        }

    }
}
