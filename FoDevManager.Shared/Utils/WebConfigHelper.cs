using FoDevManager.Messages;
using System;
using System.IO;
using System.Linq;
using System.Xml.Linq;

namespace FODevManager.Utils
{
    public static class WebConfigHelper
    {
        private const string WebConfigPath = @"C:\\AOSService\\WebRoot\\web.config";

        public static string? GetCurrentDatabaseName()
        {
            if (!File.Exists(WebConfigPath))
                return null;

            try
            {
                var xml = XDocument.Load(WebConfigPath);
                var dbKey = xml.Descendants("add")
                    .FirstOrDefault(e => e.Attribute("key")?.Value == "DataAccess.Database");

                return dbKey?.Attribute("value")?.Value;
            }
            catch
            {
                return null;
            }
        }

        public static void UpdateWebConfigDatabase(string dbName)
        {
            if (!File.Exists(WebConfigPath))
            {
                MessageLogger.Warning("❌ web.config not found.");
                return;
            }

            try
            {
                var xml = XDocument.Load(WebConfigPath);
                var dbKey = xml.Descendants("add")
                    .FirstOrDefault(e => e.Attribute("key")?.Value == "DataAccess.Database");

                if (dbKey == null)
                {
                    MessageLogger.Warning("❌ 'DataAccess.Database' key not found in web.config.");
                    return;
                }

                string? currentDb = dbKey.Attribute("value")?.Value;
                if (currentDb == dbName)
                {
                    MessageLogger.Info($"ℹ️ Database is already set to '{dbName}'. No change needed.");
                    return;
                }

                dbKey.SetAttributeValue("value", dbName);
                xml.Save(WebConfigPath);
                MessageLogger.Info($"🔄 Database switched to: {dbName}");
            }
            catch (Exception ex)
            {
                MessageLogger.Error($"❌ Failed to update web.config: {ex.Message}");
            }
        }
    }
}
