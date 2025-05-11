using FODevManager.Messages;
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
                MessageLogger.Info($"📁 Creating folder: {path}");
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

        public static string GetProjectFilePath(string modelName, string projectFilePath)
        {
            var returnPath = string.Empty;

            if (!Path.IsPathRooted(projectFilePath))
            {
                if(TryFilePath(Path.Combine(projectFilePath, modelName, $"{modelName}.rnrproj"), out returnPath))
                    return returnPath;
            }

            if (!Path.HasExtension(projectFilePath))
            {
                if (PathContainsDirectory(projectFilePath, "project") && PathContainsDirectory(projectFilePath, modelName))
                {
                    if (TryFilePath(Path.Combine(projectFilePath, $"{modelName}.rnrproj"), out returnPath))
                        return returnPath;

                }
                if (PathContainsDirectory(projectFilePath, "project") && !PathContainsDirectory(projectFilePath, modelName))
                {
                    if (TryFilePath(Path.Combine(projectFilePath, modelName, $"{modelName}.rnrproj"), out returnPath))
                        return returnPath;

                }
                if (TryFilePath(Path.Combine(projectFilePath, modelName, $"{modelName}.rnrproj"), out returnPath))
                    return returnPath;

                if (TryFilePath(Path.Combine(projectFilePath, "project", modelName, $"{modelName}.rnrproj"), out returnPath))
                    return returnPath;

                if (TryFilePath(Path.Combine(projectFilePath, "project", modelName, modelName, $"{modelName}.rnrproj"), out returnPath))
                    return returnPath;
            }
            throw new FileNotFoundException($"Can't find project file for model {modelName}");
        }

        public static bool TryFilePath(string path, out string existingPath)
        {
            if (File.Exists(path))
            {
                existingPath = path;
                return true;
            }

            existingPath = string.Empty;
            return false;
        }


        public static string GetMetadataFolder(string modelName, string projectFilePath)
        {
            string? currentPath = Path.HasExtension(projectFilePath) ? Path.GetDirectoryName(projectFilePath) : projectFilePath;

            // Move up to 3 levels and check for Metadata folder
            for (int i = 0; i < 3; i++)
            {
                if (currentPath.IsNullOrEmpty()) 
                    break;
                
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

            throw new DirectoryNotFoundException($"Can't find metadata folder in path {projectFilePath}");
        }

        public static string GetModelRootFolder(string projectFilePath)
        {
            string? currentPath = Path.HasExtension(projectFilePath) ? Path.GetDirectoryName(projectFilePath) : projectFilePath;

            // Move up to 4 levels and check for Metadata folder
            for (int i = 0; i < 4; i++)
            {
                if (currentPath.IsNullOrEmpty()) break;

                string metadataPath = Path.Combine(currentPath, "Project");
                if (Directory.Exists(metadataPath))
                {
                    return currentPath;
                }

                // Move one level up
                currentPath = Directory.GetParent(currentPath)?.FullName;
            }

            throw new DirectoryNotFoundException($"Can't find model root of project {projectFilePath}");
        }
        static string GetParentDirectory(string path)
        {
            DirectoryInfo parentDir = Directory.GetParent(Path.GetDirectoryName(path));
            return parentDir?.FullName + Path.DirectorySeparatorChar;
        }

    }
}
