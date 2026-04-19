using Microsoft.Extensions.Configuration;
using System;

namespace FODevManager.Utils
{
    public class AppConfig
    {
        public const string DefaultProfileStoragePath = "%APPDATA%\\FODevManager";
        public const string DefaultDeploymentBasePath = "C:\\AOSService\\PackagesLocalDirectory";
        public const string DefaultDeployablePackagesPath = "%APPDATA%\\FODevManager\\DeployablePackages";
        public const string DefaultSourceDirectoryPath = "C:\\Dev";
        public const string DefaultTaskUrl = "https://tasks.peritus.no/nb-NO/Case/Details/";
        public const int DefaultModelIdBegin = 896001001;
        public const int DefaultModelIdEnd = 896009999;

        public string ProfileStoragePath { get; set; }
        public string DeploymentBasePath { get; set; }
        public string DeployablePackages { get; set; }
        public string DefaultSourceDirectory { get; set; }

        public string TaskUrl { get; set; }
        public string AzureArtifactsUsername { get; set; } = string.Empty;
        public string AzureArtifactsPat { get; set; } = string.Empty;
        public string AzureArtifactsApiKey { get; set; } = string.Empty;
        public bool PushDeployablePackageOnBuild { get; set; } = false;
        public string PushDeployablePackageSource { get; set; } = string.Empty;

        public int ModelIdBegin { get; set; }

        public int ModelIdEnd { get; set; }

        public bool CheckUncommittedBeforeSwitch { get; set; } = true;

        public AppConfig()
        {

        }

        public AppConfig(IConfiguration configuration)
        {
            ProfileStoragePath = ExpandOrDefault(configuration["ProfileStoragePath"], DefaultProfileStoragePath);
            DeploymentBasePath = ExpandOrDefault(configuration["DeploymentBasePath"], DefaultDeploymentBasePath);
            DeployablePackages = ExpandOrDefault(configuration["DeployablePackages"], DefaultDeployablePackagesPath);
            DefaultSourceDirectory = ExpandOrDefault(configuration["DefaultSourceDirectory"], DefaultSourceDirectoryPath);
            TaskUrl = ExpandOrDefault(configuration["TaskUrl"], DefaultTaskUrl);
            AzureArtifactsUsername = Environment.ExpandEnvironmentVariables(configuration["AzureArtifactsUsername"] ?? string.Empty);
            AzureArtifactsPat = Environment.ExpandEnvironmentVariables(configuration["AzureArtifactsPat"] ?? string.Empty);
            AzureArtifactsApiKey = Environment.ExpandEnvironmentVariables(configuration["AzureArtifactsApiKey"] ?? string.Empty);
            PushDeployablePackageSource = Environment.ExpandEnvironmentVariables(configuration["PushDeployablePackageSource"] ?? string.Empty);
            ModelIdBegin = ReadInt(configuration, "ModelIdBegin", DefaultModelIdBegin, "ModelIdStart");
            ModelIdEnd = ReadInt(configuration, "ModelIdEnd", DefaultModelIdEnd);

            var toggle = Environment.ExpandEnvironmentVariables(configuration["CheckUncommittedBeforeSwitch"]);
            if (bool.TryParse(toggle, out var onOff)) CheckUncommittedBeforeSwitch = onOff;

            var pushDeployablePackageOnBuild = Environment.ExpandEnvironmentVariables(configuration["PushDeployablePackageOnBuild"]);
            if (bool.TryParse(pushDeployablePackageOnBuild, out var pushOnBuild))
                PushDeployablePackageOnBuild = pushOnBuild;

        }

        private static string ExpandOrDefault(string? value, string fallback)
        {
            return Environment.ExpandEnvironmentVariables(
                string.IsNullOrWhiteSpace(value) ? fallback : value);
        }

        private static int ReadInt(IConfiguration configuration, string key, int fallback, params string[] aliases)
        {
            if (TryReadInt(configuration[key], out var value))
                return value;

            foreach (var alias in aliases)
            {
                if (TryReadInt(configuration[alias], out value))
                    return value;
            }

            return fallback;
        }

        private static bool TryReadInt(string? rawValue, out int value)
        {
            value = 0;
            if (string.IsNullOrWhiteSpace(rawValue))
                return false;

            return int.TryParse(Environment.ExpandEnvironmentVariables(rawValue), out value);
        }
    }
}



