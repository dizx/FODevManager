using FODevManager.Messages;
using FODevManager.Utils;
using Microsoft.Extensions.Configuration;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace FODevManager.Shared.Utils
{
    public class AppConfigWriter
    {
        private readonly string _configFilePath;
        private readonly AppConfig _config;

        public AppConfigWriter(IConfiguration configuration)
        {
            _config = new AppConfig(configuration);
            _configFilePath = Path.Combine(AppContext.BaseDirectory, "appsettings.json");
        }

        public AppConfig GetConfig() => _config;

        public void UpdateSetting(string key, object value)
        {
            var json = File.ReadAllText(_configFilePath);
            var doc = JsonSerializer.Deserialize<Dictionary<string, object>>(json);

            if (doc.ContainsKey(key))
                doc[key] = value;
            else
                doc.Add(key, value);

            File.WriteAllText(_configFilePath, JsonSerializer.Serialize(doc, new JsonSerializerOptions { WriteIndented = true }));
        }
    }

}
