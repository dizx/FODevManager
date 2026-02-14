using FODevManager.Messages;
using FODevManager.Utils;
using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization.Metadata;

namespace FODevManager.Shared.Utils
{
    public class AppConfigWriter
    {
        private readonly string _configFilePath;
        private readonly AppConfig _config;
        private readonly object _writeLock = new();

        public AppConfigWriter(AppConfig appConfig)
        {
            _config = appConfig ?? throw new ArgumentNullException(nameof(appConfig));
            _configFilePath = Path.Combine(AppContext.BaseDirectory, "appsettings.json");
        }

        public AppConfig GetConfig() => _config;

        public void UpdateSetting(string key, object value)
        {
            if (key.IsNullOrEmpty())
                throw new ArgumentException("Key cannot be empty.", nameof(key));

            try
            {
                lock (_writeLock)
                {
                    var doc = FileHelper.LoadJson<Dictionary<string, object>>(_configFilePath)
                      ?? new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);

                    // Upsert the key
                    doc[key] = value;

                    // Save back via your FileHelper
                    FileHelper.SaveJson(_configFilePath, doc);

                    // Keep in-memory AppConfig aligned
                    switch (key)
                    {
                        case nameof(AppConfig.DefaultSourceDirectory):
                            _config.DefaultSourceDirectory = Convert.ToString(value) ?? string.Empty;
                            break;
                        case nameof(AppConfig.CheckUncommittedBeforeSwitch):
                             _config.CheckUncommittedBeforeSwitch = Convert.ToBoolean(value);
                             break;
                        case nameof(AppConfig.UseEasyGit):
                            _config.UseEasyGit = Convert.ToBoolean(value);
                            break;
                        case nameof(AppConfig.GitAutoSyncIntervalMinutes):
                            _config.GitAutoSyncIntervalMinutes = Convert.ToInt32(value);
                            break;
                        case nameof(AppConfig.ProtectedBranches):
                            _config.ProtectedBranches = Convert.ToString(value) ?? _config.ProtectedBranches;
                            break;
                        case nameof(AppConfig.AiAutoResolveConfidenceThreshold):
                            _config.AiAutoResolveConfidenceThreshold = Convert.ToDouble(value);
                            break;
                        case nameof(AppConfig.AzureOpenAiEndpoint):
                            _config.AzureOpenAiEndpoint = Convert.ToString(value) ?? string.Empty;
                            break;
                        case nameof(AppConfig.AzureOpenAiDeployment):
                            _config.AzureOpenAiDeployment = Convert.ToString(value) ?? string.Empty;
                            break;
                        case nameof(AppConfig.AzureOpenAiApiKey):
                            _config.AzureOpenAiApiKey = Convert.ToString(value) ?? string.Empty;
                            break;
                        case nameof(AppConfig.AzureDevOpsOrganizationUrl):
                            _config.AzureDevOpsOrganizationUrl = Convert.ToString(value) ?? string.Empty;
                            break;
                        case nameof(AppConfig.AzureDevOpsPat):
                            _config.AzureDevOpsPat = Convert.ToString(value) ?? string.Empty;
                            break;
                        case nameof(AppConfig.ProfileStoragePath):
                            _config.ProfileStoragePath = Convert.ToString(value) ?? _config.ProfileStoragePath;
                            break;
                        case nameof(AppConfig.DeploymentBasePath):
                            _config.DeploymentBasePath = Convert.ToString(value) ?? _config.DeploymentBasePath;
                            break;
                        case nameof(AppConfig.ModelIdBegin):
                            _config.ModelIdBegin = Convert.ToInt32(value);
                            break;
                        case nameof(AppConfig.ModelIdEnd):
                            _config.ModelIdEnd = Convert.ToInt32(value);
                            break;
                        case nameof(AppConfig.TaskUrl):
                            _config.TaskUrl = Convert.ToString(value) ?? _config.TaskUrl;
                            break;
                        default:
                            // unknown key: JSON updated; no in-memory mapping
                            break;
                    }

                    MessageLogger.Info($"Config updated: {key} = {value}");
                }
            }
            catch (Exception ex)
            {
                MessageLogger.Error($"Failed to update config '{key}': {ex.Message}");
                throw;
            }
        }
    }
}
