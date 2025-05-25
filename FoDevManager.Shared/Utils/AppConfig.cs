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

        public int ModelIdBegin { get; set; }

        public int ModelIdEnd { get; set; }

        public AppConfig(IConfiguration configuration)
        {
            ProfileStoragePath = Environment.ExpandEnvironmentVariables(configuration["ProfileStoragePath"]);
            DeploymentBasePath = Environment.ExpandEnvironmentVariables(configuration["DeploymentBasePath"]);
            DefaultSourceDirectory = Environment.ExpandEnvironmentVariables(configuration["DefaultSourceDirectory"]);
            ModelIdBegin = int.Parse(Environment.ExpandEnvironmentVariables(configuration["ModelIdBegin"] ?? "896001001"));
            ModelIdEnd = int.Parse(Environment.ExpandEnvironmentVariables(configuration["ModelIdEnd"] ?? "896009999"));

        }
    }
}
