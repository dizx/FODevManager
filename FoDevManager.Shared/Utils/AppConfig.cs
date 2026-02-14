using Microsoft.Extensions.Configuration;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace FODevManager.Utils
{
    public class AppConfig
    {
        public string ProfileStoragePath { get; set; }
        public string DeploymentBasePath { get; set; }
        public string DefaultSourceDirectory { get; set; }

        public string TaskUrl { get; set; }

        public int ModelIdBegin { get; set; }

        public int ModelIdEnd { get; set; }

        public bool CheckUncommittedBeforeSwitch { get; set; } = true;

        public bool UseEasyGit { get; set; } = false;

        public int GitAutoSyncIntervalMinutes { get; set; } = 5;

        public string ProtectedBranches { get; set; } = "main,master";

        public double AiAutoResolveConfidenceThreshold { get; set; } = 0.85;

        public string AzureOpenAiEndpoint { get; set; } = string.Empty;

        public string AzureOpenAiDeployment { get; set; } = string.Empty;

        public string AzureOpenAiApiKey { get; set; } = string.Empty;

        public string AzureDevOpsOrganizationUrl { get; set; } = string.Empty;

        public string AzureDevOpsPat { get; set; } = string.Empty;

        public AppConfig()
        {

        }

        public AppConfig(IConfiguration configuration)
        {
            ProfileStoragePath = Environment.ExpandEnvironmentVariables(configuration["ProfileStoragePath"]);
            DeploymentBasePath = Environment.ExpandEnvironmentVariables(configuration["DeploymentBasePath"]);
            DefaultSourceDirectory = Environment.ExpandEnvironmentVariables(configuration["DefaultSourceDirectory"]);
            TaskUrl = Environment.ExpandEnvironmentVariables(configuration["TaskUrl"]);
            ModelIdBegin = int.Parse(Environment.ExpandEnvironmentVariables(configuration["ModelIdBegin"] ?? "896001001"));
            ModelIdEnd = int.Parse(Environment.ExpandEnvironmentVariables(configuration["ModelIdEnd"] ?? "896009999"));

            var toggle = Environment.ExpandEnvironmentVariables(configuration["CheckUncommittedBeforeSwitch"]);
            if (bool.TryParse(toggle, out var onOff)) CheckUncommittedBeforeSwitch = onOff;

            var useEasyGit = Environment.ExpandEnvironmentVariables(configuration["UseEasyGit"]);
            if (bool.TryParse(useEasyGit, out var easyGitEnabled)) UseEasyGit = easyGitEnabled;

            var syncInterval = Environment.ExpandEnvironmentVariables(configuration["GitAutoSyncIntervalMinutes"]);
            if (int.TryParse(syncInterval, out var syncMinutes) && syncMinutes > 0)
                GitAutoSyncIntervalMinutes = syncMinutes;

            ProtectedBranches = Environment.ExpandEnvironmentVariables(configuration["ProtectedBranches"] ?? ProtectedBranches);

            var threshold = Environment.ExpandEnvironmentVariables(configuration["AiAutoResolveConfidenceThreshold"]);
            if (double.TryParse(threshold, out var confidenceThreshold) && confidenceThreshold > 0)
                AiAutoResolveConfidenceThreshold = confidenceThreshold;

            AzureOpenAiEndpoint = Environment.ExpandEnvironmentVariables(configuration["AzureOpenAiEndpoint"] ?? string.Empty);
            AzureOpenAiDeployment = Environment.ExpandEnvironmentVariables(configuration["AzureOpenAiDeployment"] ?? string.Empty);
            AzureOpenAiApiKey = Environment.ExpandEnvironmentVariables(configuration["AzureOpenAiApiKey"] ?? string.Empty);
            AzureDevOpsOrganizationUrl = Environment.ExpandEnvironmentVariables(configuration["AzureDevOpsOrganizationUrl"] ?? string.Empty);
            AzureDevOpsPat = Environment.ExpandEnvironmentVariables(configuration["AzureDevOpsPat"] ?? string.Empty);

        }
    }
}
