using System;
using System.Diagnostics;
using System.IO;
using System.Text.RegularExpressions;

namespace FODevManager.Utils
{
    public static class GitHelper
    {
        public static bool IsGitRepository(string repoPath)
        {
            string gitDirPath = Path.Combine(repoPath, ".git");
            string configPath = Path.Combine(gitDirPath, "config");

            if (!Directory.Exists(gitDirPath) || !File.Exists(configPath))
                return false;

            string[] lines = File.ReadAllLines(configPath);
            foreach (string line in lines)
            {
                if (line.Trim().StartsWith("[remote \"origin\"]"))
                    return true;
            }

            return false;
        }

        public static void OpenGitRemoteUrl(string repoPath)
        {
            string configPath = Path.Combine(repoPath, ".git", "config");

            if (!File.Exists(configPath))
            {
                Console.WriteLine("❌ .git/config not found.");
                return;
            }

            string remoteUrl = GetGitRemoteUrl(configPath);
            if (string.IsNullOrEmpty(remoteUrl))
            {
                Console.WriteLine("❌ Could not find remote URL in .git/config.");
                return;
            }

            remoteUrl = ConvertToHttpsUrl(remoteUrl);
            Console.WriteLine($"🌐 Opening: {remoteUrl}");
            OpenUrl(remoteUrl);
        }

        private static string GetGitRemoteUrl(string configPath)
        {
            string[] lines = File.ReadAllLines(configPath);
            bool inRemoteSection = false;

            foreach (string line in lines)
            {
                if (line.Trim().StartsWith("[remote \"origin\"]"))
                {
                    inRemoteSection = true;
                }
                else if (inRemoteSection && line.Trim().StartsWith("url = "))
                {
                    return line.Split('=')[1].Trim();
                }
            }
            return null;
        }

        private static string ConvertToHttpsUrl(string url)
        {
            if (url.StartsWith("git@"))
                return Regex.Replace(url, @"git@([^:]+):(.+).git", "https://$1/$2");
            return url;
        }

        private static void OpenUrl(string url)
        {
            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = url,
                    UseShellExecute = true
                });
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ Failed to open URL: {ex.Message}");
            }
        }
    }
}
