using System;
using System.Diagnostics;
using System.IO;
using System.Text.RegularExpressions;
using FODevManager.Messages;

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
                MessageLogger.Error("❌ .git/config not found.");
                return;
            }

            string remoteUrl = GetGitRemoteUrl(configPath);
            if (string.IsNullOrEmpty(remoteUrl))
            {
                MessageLogger.Error("❌ Could not find remote URL in .git/config.");
                return;
            }

            remoteUrl = ConvertToHttpsUrl(remoteUrl);
            MessageLogger.Info($"🌐 Opening: {remoteUrl}");
            OpenUrl(remoteUrl);
        }

        public static string? GetActiveBranch(string repoPath)
        {
            try
            {
;               var result = string.Empty;
                if (RunProcess(repoPath, "git", "rev-parse --abbrev-ref HEAD", out result))
                    return result;
            }
            catch (Exception ex)
            {
                MessageLogger.Error($"Error fetching branch: {ex.Message}");
            }

            return null;
        }

        public static bool HasUncommittedChanges(string repoPath)
        {
            try
            {
                string result;
                if (RunProcess(repoPath, "git", "status --porcelain", out result))
                {
                    return !string.IsNullOrWhiteSpace(result);
                }
            }
            catch (Exception ex)
            {
                MessageLogger.Error($"Error checking for uncommitted changes: {ex.Message}");
            }

            return false;
        }



        private static bool RunProcess(string workingDir, string fileName, string args)
        {
            var result = string.Empty;
            return RunProcess(workingDir, fileName, args, out result);

        }

        private static bool RunProcess(string workingDir, string fileName, string args, out string result)
        {
            result = string.Empty;

            ProcessStartInfo psi = new()
            {
                FileName = fileName,
                Arguments = args,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
                WorkingDirectory = workingDir
            };

            using Process process = new Process { StartInfo = psi };
            
            process.Start();
            string output = process.StandardOutput.ReadToEnd() ?? string.Empty;
            string error = process.StandardError.ReadToEnd();

            process.WaitForExit();

            result = (output + "\n" + error).Trim();

            return process.ExitCode == 0;

        }

        public static bool FetchFromRemote(string profileName, string repoPath)
        {
                if (!IsGitRepository(repoPath))
                return false;

            try
            {
                var result = string.Empty;
                if(RunProcess(repoPath, "git", "fetch --all"))
                {
                    if(RunProcess(repoPath, "git", "status -sb", out result))
                    {
                        MessageLogger.Info($"Model {profileName }: {result}");
                        return true;
                    }
                }
                return false;
            }
            catch (Exception ex)
            {
                MessageLogger.Error($"Error fetching from remote: {ex.Message}");
                return false;
            }
        }

        public static bool CloneRepository(string gitUrl, string targetPath)
        {
            try
            {
                if (Directory.Exists(Path.Combine(targetPath, ".git")))
                {
                    MessageLogger.Warning($"⚠️ Git repository already exists at {targetPath}. Skipping clone.");
                    return false;
                }

                string result;
                MessageLogger.Info($"🌀 Cloning '{gitUrl}' into '{targetPath}'...");

                if (RunProcess(Directory.GetParent(targetPath).FullName, "git", $"clone {gitUrl} \"{targetPath}\"", out result))
                {
                    MessageLogger.Highlight($"✅ Successfully cloned {gitUrl}");
                    return true;
                }

                MessageLogger.Error($"❌ Failed to clone {gitUrl}");
                return false;
            }
            catch (Exception ex)
            {
                MessageLogger.Error($"❌ Error during Git clone: {ex.Message}");
                return false;
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
                MessageLogger.Error($"❌ Failed to open URL: {ex.Message}");
            }
        }
    }
}
