using FODevManager.Messages;
using FODevManager.Utils;
using System;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace FODevManager.Shared.Utils
{
    public static class AppSettingsMigration
    {
        public static void RunOnStartup()
        {
            var configFilePath = Path.Combine(AppContext.BaseDirectory, "appsettings.json");

            try
            {
                JsonObject config = LoadJsonObjectOrDefault(configFilePath);
                var changes = 0;

                changes += RenameProperty(config, "PeriTaskUrl", nameof(AppConfig.TaskUrl));
                changes += RenameProperty(config, "ModelIdStart", nameof(AppConfig.ModelIdBegin));
                changes += CopyMissingProperties(CreateDefaultConfig(), config);

                if (!File.Exists(configFilePath) || changes > 0)
                {
                    var json = config.ToJsonString(new JsonSerializerOptions
                    {
                        WriteIndented = true
                    });

                    File.WriteAllText(configFilePath, json, new UTF8Encoding(false));
                }

                if (changes > 0)
                    MessageLogger.Highlight($"Config migration applied: {changes} setting update(s)");
            }
            catch (Exception exception)
            {
                // Migration must not block startup
                MessageLogger.Warning($"Config migration failed. Continuing without migration. {exception.Message}");
            }
        }

        private static JsonObject LoadJsonObjectOrDefault(string path)
        {
            if (!File.Exists(path))
                return CreateDefaultConfig();

            var text = File.ReadAllText(path, Encoding.UTF8);
            if (string.IsNullOrWhiteSpace(text))
                return CreateDefaultConfig();

            return JsonNode.Parse(text) as JsonObject ?? CreateDefaultConfig();
        }

        private static JsonObject CreateDefaultConfig()
        {
            return new JsonObject
            {
                [nameof(AppConfig.ProfileStoragePath)] = AppConfig.DefaultProfileStoragePath,
                [nameof(AppConfig.DeploymentBasePath)] = AppConfig.DefaultDeploymentBasePath,
                [nameof(AppConfig.DefaultSourceDirectory)] = AppConfig.DefaultSourceDirectoryPath,
                [nameof(AppConfig.DeployablePackages)] = AppConfig.DefaultDeployablePackagesPath,
                [nameof(AppConfig.ModelIdBegin)] = AppConfig.DefaultModelIdBegin,
                [nameof(AppConfig.ModelIdEnd)] = AppConfig.DefaultModelIdEnd,
                [nameof(AppConfig.TaskUrl)] = AppConfig.DefaultTaskUrl,
                [nameof(AppConfig.CheckUncommittedBeforeSwitch)] = false,
                [nameof(AppConfig.AzureArtifactsUsername)] = string.Empty,
                [nameof(AppConfig.AzureArtifactsPat)] = string.Empty,
                [nameof(AppConfig.AzureArtifactsApiKey)] = string.Empty,
                [nameof(AppConfig.NuGetExecutablePath)] = string.Empty,
                [nameof(AppConfig.PushDeployablePackageOnBuild)] = false,
                [nameof(AppConfig.PushDeployablePackageSource)] = string.Empty
            };
        }

        private static int RenameProperty(JsonObject config, string oldName, string newName)
        {
            if (!config.TryGetPropertyValue(oldName, out var value) || value == null)
                return 0;

            if (!config.ContainsKey(newName))
                config[newName] = value.DeepClone();

            config.Remove(oldName);
            return 1;
        }

        private static int CopyMissingProperties(JsonObject source, JsonObject target)
        {
            var changes = 0;

            foreach (var property in source)
            {
                if (target.ContainsKey(property.Key))
                    continue;

                target[property.Key] = property.Value?.DeepClone();
                changes++;
            }

            return changes;
        }
    }
}
