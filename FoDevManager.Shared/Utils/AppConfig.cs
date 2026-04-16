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
            ProfileStoragePath = Environment.ExpandEnvironmentVariables(configuration["ProfileStoragePath"]);
            DeploymentBasePath = Environment.ExpandEnvironmentVariables(configuration["DeploymentBasePath"]);
            DeployablePackages = Environment.ExpandEnvironmentVariables(configuration["DeployablePackages"] ?? string.Empty);
            DefaultSourceDirectory = Environment.ExpandEnvironmentVariables(configuration["DefaultSourceDirectory"]);
            TaskUrl = Environment.ExpandEnvironmentVariables(configuration["TaskUrl"]);
            AzureArtifactsUsername = Environment.ExpandEnvironmentVariables(configuration["AzureArtifactsUsername"] ?? string.Empty);
            AzureArtifactsPat = Environment.ExpandEnvironmentVariables(configuration["AzureArtifactsPat"] ?? string.Empty);
            AzureArtifactsApiKey = Environment.ExpandEnvironmentVariables(configuration["AzureArtifactsApiKey"] ?? string.Empty);
            PushDeployablePackageSource = Environment.ExpandEnvironmentVariables(configuration["PushDeployablePackageSource"] ?? string.Empty);
            ModelIdBegin = int.Parse(Environment.ExpandEnvironmentVariables(configuration["ModelIdBegin"] ?? "896001001"));
            ModelIdEnd = int.Parse(Environment.ExpandEnvironmentVariables(configuration["ModelIdEnd"] ?? "896009999"));

            var toggle = Environment.ExpandEnvironmentVariables(configuration["CheckUncommittedBeforeSwitch"]);
            if (bool.TryParse(toggle, out var onOff)) CheckUncommittedBeforeSwitch = onOff;

            var pushDeployablePackageOnBuild = Environment.ExpandEnvironmentVariables(configuration["PushDeployablePackageOnBuild"]);
            if (bool.TryParse(pushDeployablePackageOnBuild, out var pushOnBuild))
                PushDeployablePackageOnBuild = pushOnBuild;

        }
    }
}



