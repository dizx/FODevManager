using System;
using System.Diagnostics;
using System.IO;
using System.Text.RegularExpressions;
using FODevManager.Messages;

namespace FODevManager.Utils
{
    public static class GitHelper
    {
        public sealed class GitRepoState
        {
            public string? Branch { get; init; }
            public string? Commit { get; init; }
            public bool IsDirty { get; init; }
        }

        public static GitRepoState GetRepoState(string repoPath)
        {
            var branch = GetActiveBranch(repoPath);
            var commit = GetHeadCommit(repoPath);
            var dirty = HasUncommittedChanges(repoPath);

            return new GitRepoState
            {
                Branch = branch,
                Commit = commit,
                IsDirty = dirty
            };
        }

        public static string? GetHeadCommit(string repoPath)
        {
            try
            {
                string result;
                if (RunProcess(repoPath, "git", "rev-parse HEAD", out result))
                    return result?.Trim();
            }
            catch (Exception ex)
            {
                MessageLogger.Error($"Error fetching commit: {ex.Message}");
            }

            return null;
        }

        public static bool Stash(string repoPath, string message, bool includeUntracked = true)
        {
            try
            {
                var args = includeUntracked
                    ? $"stash push -u -m \"{EscapeQuotes(message)}\""
                    : $"stash push -m \"{EscapeQuotes(message)}\"";

                string result;
                if (RunProcess(repoPath, "git", args, out result))
                {
                    // git prints "No local changes to save" when nothing to stash
                    if (result.IndexOf("No local changes", StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        MessageLogger.Info("ℹ️ Nothing to stash.");
                        return true;
                    }

                    MessageLogger.Highlight("✅ Changes stashed.");
                    return true;
                }
            }
            catch (Exception ex)
            {
                MessageLogger.Error($"❌ Error during stash: {ex.Message}");
            }

            return false;
        }

        public static bool StashPop(string repoPath)
        {
            try
            {
                string result;
                if (RunProcess(repoPath, "git", "stash pop", out result))
                {
                    // Conflicts can still yield exit code 0 sometimes, so be conservative:
                    if (result.IndexOf("CONFLICT", StringComparison.OrdinalIgnoreCase) >= 0)
                        MessageLogger.Warning("⚠️ Stash applied with conflicts. Manual resolution may be required.");
                    else
                        MessageLogger.Highlight("✅ Stash applied.");

                    return true;
                }
            }
            catch (Exception ex)
            {
                MessageLogger.Error($"❌ Error during stash pop: {ex.Message}");
            }

            return false;
        }
        private static string EscapeQuotes(string value) => value.Replace("\"", "\\\"");



       public static bool IsGitRepository(string? repoPath)
        {
            if(repoPath.IsNullOrEmpty())
                return false;

            string noOutput = "";
            return IsGitRepository(repoPath, out noOutput);
        }

        public static bool IsGitRepository(string? repoPath, out string remoteUrl)
        {
            remoteUrl = string.Empty;

            if (repoPath.IsNullOrEmpty())
                return false;
            
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

        public static void OpenGitRemoteUrl(string? repoPath)
        {
            if (repoPath.IsNullOrEmpty())
            {
                MessageLogger.Error("❌ Repository path is null or empty.");
                return;
            }

            string configPath = Path.Combine(repoPath, ".git", "config");

            if (!File.Exists(configPath))
            {
                MessageLogger.Error("❌ .git/config not found.");
                return;
            }

            string? remoteUrl = GetGitRemoteUrl(configPath);
            if (remoteUrl.IsNullOrEmpty())
            {
                MessageLogger.Error("❌ Could not find remote URL in .git/config.");
                return;
            }

            remoteUrl = ConvertToHttpsUrl(remoteUrl);
            MessageLogger.Info($"🌐 Opening: {remoteUrl}");
            OpenUrl(remoteUrl);
        }

        public static string? GetActiveBranch(string? repoPath)
        {
            if (repoPath.IsNullOrEmpty())
            {
                MessageLogger.Error("❌ Repository path is null or empty.");
                return string.Empty;
            }

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

        public static bool ChangeBranch(string repoPath, string branchName, bool autoStashIfDirty, string? stashMessage = null)
        {
            if (!IsGitRepository(repoPath))
            {
                MessageLogger.Error("❌ Not a valid Git repository.");
                return false;
            }

            branchName = branchName?.Trim() ?? "";
            if (branchName.IsNullOrEmpty())
            {
                MessageLogger.Error("❌ Branch name is empty.");
                return false;
            }

            var state = GetRepoState(repoPath);
            if (!state.Branch.IsNullOrEmpty() &&
                string.Equals(state.Branch, branchName, StringComparison.OrdinalIgnoreCase))
            {
                MessageLogger.Info($"ℹ️ Already on branch: {branchName}");
                return true;
            }

            if (state.IsDirty)
            {
                if (!autoStashIfDirty)
                {
                    MessageLogger.Warning($"⚠️ Repo has uncommitted changes. Skipping checkout to '{branchName}'.");
                    return false;
                }

                var msg = stashMessage.IsNullOrEmpty()
                    ? $"FO Dev Manager: auto-stash before switching to {branchName}"
                    : stashMessage;

                MessageLogger.Warning($"⚠️ Repo is dirty. Stashing changes before switching to '{branchName}'.");
                if (!Stash(repoPath, msg, includeUntracked: true))
                {
                    MessageLogger.Error("❌ Stash failed. Cannot switch branch.");
                    return false;
                }
            }

            // Fetch first so origin/<branch> is known
            if (!FetchAll(repoPath))
            {
                MessageLogger.Warning("⚠️ Fetch failed (continuing anyway).");
            }

            // 1) If local branch exists: checkout
            if (LocalBranchExists(repoPath, branchName))
            {
                return Checkout(repoPath, branchName);
            }

            // 2) If remote branch exists: create tracking local branch and checkout
            if (RemoteBranchExists(repoPath, "origin", branchName))
            {
                return Checkout(repoPath, $"-b {branchName} --track origin/{branchName}");
            }

            // 3) Otherwise: do NOT create a new empty branch silently
            MessageLogger.Error($"❌ Branch '{branchName}' not found locally or on origin.");
            return false;
        }

        private static bool FetchAll(string repoPath)
        {
            try
            {
                string result;
                if (RunProcess(repoPath, "git", "fetch --all --prune", out result))
                {
                    MessageLogger.Info("✅ Fetch completed.");
                    return true;
                }
            }
            catch (Exception ex)
            {
                MessageLogger.Error($"Error fetching from remote: {ex.Message}");
            }

            return false;
        }

        private static bool Checkout(string repoPath, string checkoutArgs)
        {
            try
            {
                string result;
                if (RunProcess(repoPath, "git", $"checkout {checkoutArgs}", out result))
                {
                    MessageLogger.Highlight($"✅ Checked out: {checkoutArgs}");
                    return true;
                }

                MessageLogger.Error($"❌ Checkout failed: {checkoutArgs}");
                return false;
            }
            catch (Exception ex)
            {
                MessageLogger.Error($"❌ Error during checkout: {ex.Message}");
                return false;
            }
        }

        private static bool LocalBranchExists(string repoPath, string branchName)
        {
            // show-ref is faster/more reliable than parsing "git branch --list"
            string result;
            return RunProcess(repoPath, "git", $"show-ref --verify --quiet refs/heads/{branchName}", out result);
        }

        private static bool RemoteBranchExists(string repoPath, string remoteName, string branchName)
        {
            // Check refs/remotes/origin/<branch>
            string result;
            return RunProcess(repoPath, "git", $"show-ref --verify --quiet refs/remotes/{remoteName}/{branchName}", out result);
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

        public static string? GetGitRemoteUrl(string? configPath)
        {
            if(configPath.IsNullOrEmpty() || !File.Exists(configPath))
                return null;

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

        public static string DeriveRepoDisplayName(string repoRootFolder, string gitUrl)
        {
            // Preferred: repo name from git url
            var repoNameFromUrl = TryGetRepoNameFromGitUrl(gitUrl);
            if (!repoNameFromUrl.IsNullOrEmpty())
                return repoNameFromUrl!;

            // Fallback: folder name
            var folderName = Path.GetFileName(repoRootFolder.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
            return folderName.IsNullOrEmpty() ? "Repository" : folderName;
        }

        public static string? TryGetRepoNameFromGitUrl(string gitUrl)
        {
            if (gitUrl.IsNullOrEmpty())
                return null;

            // Handles:
            // https://dev.azure.com/org/project/_git/repo
            // https://org@dev.azure.com/org/project/_git/repo
            // git@ssh.dev.azure.com:v3/org/project/repo
            var normalized = gitUrl.Trim().Replace('\\', '/');

            var lastSegment = normalized.Split('/').LastOrDefault();
            if (lastSegment.IsNullOrEmpty())
                return null;

            return Uri.UnescapeDataString(lastSegment.Replace(".git", string.Empty, StringComparison.OrdinalIgnoreCase));
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
            string error = process.StandardError.ReadToEnd() ?? string.Empty;

            result = (output + "\n" + error).Trim();

            if (process.ExitCode == 0)
            {
                if (!error.IsNullOrEmpty())
                    MessageLogger.Info(error); // not error, just info
                return true;
            }

            if (!error.IsNullOrEmpty())
            {
                MessageLogger.Error(error);
                return false;
            }

            result = output.Trim();

            return true;
        }


    }
}
