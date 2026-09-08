using FODevManager.Messages;
using FODevManager.Models;
using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

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

        public static void CopyDirectory(string sourceDir, string targetDir)
        {
            foreach (string dirPath in Directory.GetDirectories(sourceDir, "*", SearchOption.AllDirectories))
            {
                Directory.CreateDirectory(dirPath.Replace(sourceDir, targetDir));
            }

            foreach (string filePath in Directory.GetFiles(sourceDir, "*.*", SearchOption.AllDirectories))
            {
                string targetFilePath = filePath.Replace(sourceDir, targetDir);
                File.Copy(filePath, targetFilePath, overwrite: true);
            }
        }

        private static readonly JsonSerializerOptions DefaultJsonSerializerOptions = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            ReadCommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true,
            WriteIndented = true,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
        };

        public static T LoadJson<T>(string filePath) where T : new()
        {
            if (!File.Exists(filePath))
            {
                return new T();
            }
            var jsonText = File.ReadAllText(filePath);

            if (typeof(T) == typeof(ProfileModel))
                jsonText = NormalizeLegacyProfileJson(jsonText);


            var result = JsonSerializer.Deserialize<T>(jsonText, DefaultJsonSerializerOptions);
            return result ?? new T();
        }

        private static string NormalizeLegacyProfileJson(string jsonText)
        {
            if (jsonText.IsNullOrEmpty())
                return jsonText;

            var hasEnvironments = jsonText.IndexOf("\"Environments\"", StringComparison.OrdinalIgnoreCase) >= 0;
            var hasModels = jsonText.IndexOf("\"Models\"", StringComparison.OrdinalIgnoreCase) >= 0;

            if (!hasEnvironments || hasModels)
                return jsonText;

            jsonText = Regex.Replace(
                jsonText,
                "\"Environments\"\\s*:",
                "\"StandaloneModels\":",
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

            jsonText = Regex.Replace(
                jsonText,
                "\"PeriTask\"\\s*:",
                "\"Task\":",
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

          
            return jsonText;
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

            if (Directory.Exists(projectFilePath) || !Path.HasExtension(projectFilePath))
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


        public static string GetMetadataFolder(string modelName, string modelPath)
        {
            string? currentPath = Directory.Exists(modelPath) || !Path.HasExtension(modelPath)
                ? modelPath
                : Path.GetDirectoryName(modelPath);

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

            throw new DirectoryNotFoundException($"Can't find metadata folder in path {modelPath}");
        }

        public static string GetLibsFolder(string modelName, string modelPath)
        {
            if (modelName.IsNullOrEmpty())
                throw new ArgumentException("Model name is required", nameof(modelName));

            if (modelPath.IsNullOrEmpty())
                throw new ArgumentException("Model path is required", nameof(modelPath));

            string? currentPath = Directory.Exists(modelPath) || !Path.HasExtension(modelPath)
                ? modelPath
                : Path.GetDirectoryName(modelPath);

            var libFolderNames = new[] { "Libs", "Lib" };

            for (int levelIndex = 0; levelIndex < 3; levelIndex++)
            {
                if (currentPath.IsNullOrEmpty())
                    break;

                foreach (var libFolderName in libFolderNames)
                {
                    var libsWithModelPath = Path.Combine(currentPath, libFolderName, modelName);
                    if (Directory.Exists(libsWithModelPath))
                        return libsWithModelPath;

                    var libsRootPath = Path.Combine(currentPath, libFolderName);
                    if (Directory.Exists(libsRootPath))
                        return Path.Combine(libsRootPath, modelName);
                }

                currentPath = Directory.GetParent(currentPath)?.FullName;
            }

            throw new DirectoryNotFoundException($"Can't find libs folder in path {modelPath}");
        }


        public static string GetModelRootFolder(string projectFilePath)
        {
            string? currentPath = Directory.Exists(projectFilePath) || !Path.HasExtension(projectFilePath)
                ? projectFilePath
                : Path.GetDirectoryName(projectFilePath);

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
