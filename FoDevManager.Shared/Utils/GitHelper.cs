﻿using System;
using System.Diagnostics;
using System.IO;
using System.Text.RegularExpressions;
using FoDevManager.Messages;

namespace FODevManager.Utils
{
    public static class GitHelper
    {

        public static bool IsGitRepository(string repoPath)
        {
            string noOutput = "";
            return IsGitRepository(repoPath, out noOutput);
        }

        public static bool IsGitRepository(string repoPath, out string remoteUrl)
        {
            remoteUrl = string.Empty;
            var isGitRepo = false; ;
            string gitDirPath = Path.Combine(repoPath, ".git");
            string configPath = Path.Combine(gitDirPath, "config");

            if (!Directory.Exists(gitDirPath) || !File.Exists(configPath))
                return false;

            string[] lines = File.ReadAllLines(configPath);
            foreach (string line in lines)
            {
                if (line.Trim().StartsWith("[remote \"origin\"]"))
                {
                    isGitRepo = true;
                }
            }

            if (isGitRepo)
            {
                remoteUrl = GetGitRemoteUrl(configPath);
            }

            return isGitRepo;
        }

        public static void OpenGitRemoteUrl(string repoPath)
        {
            string configPath = Path.Combine(repoPath, ".git", "config");

            if (!File.Exists(configPath))
            {
                MessageLogger.Write("❌ .git/config not found.");
                return;
            }

            string remoteUrl = GetGitRemoteUrl(configPath);
            if (string.IsNullOrEmpty(remoteUrl))
            {
                MessageLogger.Write("❌ Could not find remote URL in .git/config.");
                return;
            }

            remoteUrl = ConvertToHttpsUrl(remoteUrl);
            MessageLogger.Write($"🌐 Opening: {remoteUrl}");
            OpenUrl(remoteUrl);
        }

        public static string? GetActiveBranch(string repoPath)
        {
            try
            {
                ProcessStartInfo psi = new ProcessStartInfo
                {
                    FileName = "git",
                    Arguments = "rev-parse --abbrev-ref HEAD",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    WorkingDirectory = repoPath
                };

                using (Process process = new Process { StartInfo = psi })
                {
                    process.Start();
                    string output = process.StandardOutput.ReadLine() ?? string.Empty;
                    process.WaitForExit();

                    if (string.IsNullOrWhiteSpace(output))
                    {
                        MessageLogger.Error("Could not determine the branch.");
                        return null;
                    }

                    return output.Trim();
                }
            }
            catch (Exception ex)
            {
                MessageLogger.Error($"Error fetching branch: {ex.Message}");
                return null;
            }
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
                MessageLogger.Write($"❌ Failed to open URL: {ex.Message}");
            }
        }
    }
}
