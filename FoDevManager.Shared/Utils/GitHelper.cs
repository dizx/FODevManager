using FODevManager.Messages;
using FODevManager.Shared.Models;
using FODevManager.Shared.Utils;
using System;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Text.RegularExpressions;

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
                if (RunGitCommand(repoPath, "rev-parse HEAD", out result))
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
                if (RunGitCommand(repoPath, args, out result))
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
                if (RunGitCommand(repoPath, "stash pop", out result))
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
            if (repoPath.IsNullOrEmpty())
                return false;

            string? noOutput = "";
            return IsGitRepository(repoPath, out noOutput);
        }

        public static bool IsGitRepository(string? repoPath, out string? remoteUrl)
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
                ; var result = string.Empty;
                if (RunGitCommand(repoPath, "rev-parse --abbrev-ref HEAD", out result))
                    return result.Trim();
            }
            catch (Exception ex)
            {
                MessageLogger.Error($"Error fetching branch: {ex.Message}");
            }

            return null;
        }

        public static bool IsWorkingTreeDirty(string repositoryRootFolder)
        {
            if (!RunGitCommand(
                    repositoryRootFolder,
                    "status --porcelain",
                    out var output,
                    logOnSuccess: false,
                    logOnFailure: false))
                return false;

            return !output.Trim().IsNullOrEmpty();
        }

        public static RepoBranchHealth GetBranchHealth(string repositoryRootFolder)
        {
            if (RequiresAttention(repositoryRootFolder))
                return RepoBranchHealth.NeedsAttention;

            if (IsWorkingTreeDirty(repositoryRootFolder))
                return RepoBranchHealth.Dirty;

            return RepoBranchHealth.Clean;
        }

        public static bool RequiresAttention(string repositoryRootFolder)
        {
            // Merge conflicts: unmerged files
            if (RunGitCommand(
                    repositoryRootFolder,
                    "diff --name-only --diff-filter=U",
                    out var conflicts,
                    logOnSuccess: false,
                    logOnFailure: false))
            {
                if (!conflicts.Trim().IsNullOrEmpty())
                    return true;
            }

            // Merge in progress (MERGE_HEAD exists)
            if (RunGitCommand(
                    repositoryRootFolder,
                    "rev-parse -q --verify MERGE_HEAD",
                    out _,
                    logOnSuccess: false,
                    logOnFailure: false))
            {
                return true;
            }

            return false;
        }

        public static bool HasMainChanges(string repositoryRootFolder, string mainBranchName = "main")
        {
            return HasMainChangesAsync(repositoryRootFolder, mainBranchName, CancellationToken.None)
                .GetAwaiter()
                .GetResult();
        }


        public static async Task<bool> HasMainChangesAsync(string repositoryRootFolder, string mainBranchName = "main", CancellationToken cancellationToken = default)
        {
            if (repositoryRootFolder.IsNullOrEmpty())
                return false;

            var safeMainBranchName = mainBranchName.IsNullOrEmpty() ? "main" : mainBranchName;

            var shouldFetch = Singleton<GitFetchThrottle>.Instance.ShouldFetch(repositoryRootFolder);

            if (shouldFetch)
            {
                var fetchOk = await FetchAllAsync(repositoryRootFolder, cancellationToken).ConfigureAwait(false);
                if (!fetchOk)
                    return false;
            }

            var mergeBaseResult = await RunGitCommandAsync(
                    repositoryRootFolder,
                    $"merge-base HEAD origin/{safeMainBranchName}",
                    timeout: TimeSpan.FromSeconds(20),
                    cancellationToken: cancellationToken,
                    logOnSuccess: false,
                    logOnFailure: false)
                .ConfigureAwait(false);

            if (!mergeBaseResult.Ok)
                return false;

            var mainHeadResult = await RunGitCommandAsync(
                    repositoryRootFolder,
                    $"rev-parse origin/{safeMainBranchName}",
                    timeout: TimeSpan.FromSeconds(20),
                    cancellationToken: cancellationToken,
                    logOnSuccess: false,
                    logOnFailure: false)
                .ConfigureAwait(false);

            if (!mainHeadResult.Ok)
                return false;

            return !mergeBaseResult.Output.Trim().SameAs(mainHeadResult.Output.Trim());
        }


        public static bool MergeMainIntoCurrentBranch(string repositoryRootFolder, string mainBranchName = "main")
        {
            var currentBranch = GetActiveBranch(repositoryRootFolder);

            if (currentBranch.IsNullOrEmpty())
                throw new Exception("Unable to determine current branch.");

            if (currentBranch.SameAs(mainBranchName))
                throw new Exception("Already on main branch.");

            return RunGitCommand(
                repositoryRootFolder,
                $"merge origin/{mainBranchName}",
                out _,
                logOnSuccess: true,
                logOnFailure: true);
        }


        public static bool HasUncommittedChanges(string repoPath)
        {
            try
            {
                string result;
                if (RunGitCommand(repoPath, "status --porcelain", out result))
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

        public static bool ChangeBranch(string repoPath, string branchName, bool autoStashIfDirty, string? stashMessage = null, bool createIfMissing = false)
        {
            if (!IsGitRepository(repoPath))
            {
                MessageLogger.Error("❌ Not a valid Git repository.");
                return false;
            }

            branchName = branchName?.Trim() ?? string.Empty;
            if (branchName.IsNullOrEmpty())
            {
                MessageLogger.Error("❌ Branch name is empty.");
                return false;
            }

            var state = GetRepoState(repoPath);
            if (!state.Branch.IsNullOrEmpty() &&
                state.Branch.SameAs(branchName))
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

                var message = stashMessage.IsNullOrEmpty()
                    ? $"FO Dev Manager: auto-stash before switching to {branchName}"
                    : stashMessage;

                MessageLogger.Warning($"⚠️ Repo is dirty. Stashing changes before switching to '{branchName}'.");
                if (!Stash(repoPath, message, includeUntracked: true))
                {
                    MessageLogger.Error("❌ Stash failed. Cannot switch branch.");
                    return false;
                }
            }

            // Fetch first so origin/<branch> is known (best effort)
            if (!AsyncHelpers.RunSync(() => FetchAllAsync(repoPath)))
                MessageLogger.Warning("⚠️ Fetch failed (continuing anyway).");

            if (LocalBranchExists(repoPath, branchName))
                return Checkout(repoPath, branchName);

            if (RemoteBranchExists(repoPath, "origin", branchName))
                return Checkout(repoPath, $"-b {branchName} --track origin/{branchName}");

            if (!createIfMissing)
            {
                MessageLogger.Error($"❌ Branch '{branchName}' not found locally or on origin.");
                return false;
            }

            // Create new local branch from current HEAD and switch to it
            MessageLogger.Info($"🆕 Creating new local branch '{branchName}' from current HEAD...");
            return Checkout(repoPath, $"-b {branchName}");
        }

        public static bool Pull(string repoPath, string remoteName, string branchName)
        {
            if (repoPath.IsNullOrEmpty())
            {
                MessageLogger.Error("❌ Pull: repoPath is empty.");
                return false;
            }

            remoteName = remoteName?.Trim() ?? "origin";
            if (remoteName.IsNullOrEmpty())
                remoteName = "origin";

            branchName = branchName?.Trim() ?? "";
            if (branchName.IsNullOrEmpty())
            {
                MessageLogger.Error("❌ Pull: branchName is empty.");
                return false;
            }

            try
            {
                string result;

                // ff-only = safe default; no accidental merge commits.
                if (RunGitCommand(repoPath, $"pull --ff-only {remoteName} {branchName}", out result))
                {
                    MessageLogger.Highlight($"✅ Pull completed ({remoteName}/{branchName}).");
                    return true;
                }

                MessageLogger.Error($"❌ Pull failed ({remoteName}/{branchName}).");
                return false;
            }
            catch (Exception ex)
            {
                MessageLogger.Error($"❌ Error during pull: {ex.Message}");
                return false;
            }
        }

        public static bool ResetToMainAndUpdate(string repoPath, string mainBranchName)
        {
            if (!IsGitRepository(repoPath))
            {
                MessageLogger.Warning("⚠️ ResetToMainAndUpdate: Not a Git repository.");
                return false;
            }

            var safeMainBranchName = mainBranchName.IsNullOrEmpty() ? "main" : mainBranchName;

            // Always stash if there are changes (user requested "stack it")
            if (HasUncommittedChanges(repoPath))
            {
                var stashMessage = $"FO Dev Manager: profile git reset ({DateTime.Now:yyyy-MM-dd HH:mm:ss})";
                MessageLogger.Warning("⚠️ Repo has uncommitted changes. Stashing before reset.");
                if (!Stash(repoPath, stashMessage, includeUntracked: true))
                {
                    MessageLogger.Error("❌ Stash failed. Skipping repo.");
                    return false;
                }
            }

            if (!ChangeBranch(repoPath, safeMainBranchName, autoStashIfDirty: false))
                return false;

            // Fetch + Pull
            if (!AsyncHelpers.RunSync(() => FetchAllAsync(repoPath)))
            {
                MessageLogger.Warning("⚠️ Fetch failed. Skipping pull.");
                return false;
            }

            return Pull(repoPath, "origin", safeMainBranchName);
        }


        public static bool TagExists(string repoPath, string tagName)
        {
            if (repoPath.IsNullOrEmpty() || tagName.IsNullOrEmpty())
                return false;

            try
            {
                string result;
                if (!RunGitCommand(repoPath, $"tag -l {EscapeGitArg(tagName)}", out result))
                    return false;

                return result
                    .Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
                    .Any(line => line.Trim().Equals(tagName, StringComparison.Ordinal));
            }
            catch
            {
                return false;
            }
        }

        public static bool CreateTag(string repoPath, string tagName, IReadOnlyCollection<string> messageLines)
        {
            if (!IsGitRepository(repoPath))
            {
                MessageLogger.Warning("⚠️ Create Tag: Not a Git repository.");
                return false;
            }

            if (tagName.IsNullOrEmpty())
            {
                MessageLogger.Error("❌ Create Tag: tagName is empty.");
                return false;
            }

            var safeLines = (messageLines ?? Array.Empty<string>())
                .Where(line => !string.IsNullOrWhiteSpace(line))
                .Select(line => line.Trim())
                .ToList();

            if (safeLines.Count == 0)
                safeLines.Add("FO Dev Manager release tag");

            if (TagExists(repoPath, tagName))
            {
                MessageLogger.Warning($"⚠️ Tag already exists: {tagName}");
                return false;
            }

            // Build a git command like:
            // git tag -a <tag> -m "line1" -m "line2" ...
            var messageArgs = string.Join(" ", safeLines.Select(line => $"-m {EscapeGitArg(line)}"));

            try
            {
                string result;
                if (RunGitCommand(repoPath, $"tag -a {EscapeGitArg(tagName)} {messageArgs}", out result))
                {
                    MessageLogger.Highlight($"✅ Created git tag: {tagName}");
                    return true;
                }

                MessageLogger.Error($"❌ Failed to create git tag: {tagName}");
                return false;
            }
            catch (Exception exception)
            {
                MessageLogger.Error($"❌ Error creating annotated tag '{tagName}': {exception.Message}");
                return false;
            }
        }

        public static bool PushTag(string repoPath, string tagName, string remoteName = "origin")
        {
            if (!IsGitRepository(repoPath))
                return false;

            if (tagName.IsNullOrEmpty())
                return false;

            remoteName = remoteName?.Trim() ?? "origin";
            if (remoteName.IsNullOrEmpty())
                remoteName = "origin";

            try
            {
                string result;
                if (RunGitCommand(repoPath, $"push {EscapeGitArg(remoteName)} {EscapeGitArg(tagName)}", out result))
                {
                    MessageLogger.Info($"⬆️ Pushed tag '{tagName}' to {remoteName}.");
                    return true;
                }

                MessageLogger.Error($"❌ Failed to push tag '{tagName}' to {remoteName}.");
                return false;
            }
            catch (Exception exception)
            {
                MessageLogger.Error($"❌ Error pushing tag '{tagName}': {exception.Message}");
                return false;
            }
        }

        public static bool IsReleaseBranchName(string? branchName)
        {
            if (string.IsNullOrWhiteSpace(branchName))
                return false;

            var trimmedBranchName = branchName.Trim();

            return trimmedBranchName.Equals("release", StringComparison.OrdinalIgnoreCase)
                   || trimmedBranchName.StartsWith("release/", StringComparison.OrdinalIgnoreCase)
                   || trimmedBranchName.StartsWith("release-", StringComparison.OrdinalIgnoreCase)
                   || trimmedBranchName.StartsWith("releases/", StringComparison.OrdinalIgnoreCase);
        }

        private static string EscapeGitArg(string value)
        {
            // RunGitCommand uses ProcessStartInfo.Arguments (string), so we need shell-style quoting.
            // This is a minimal "wrap in quotes and escape inner quotes" implementation.
            var safeValue = value?.Replace("\"", "\\\"") ?? string.Empty;
            return $"\"{safeValue}\"";
        }

        public static string GetGitUserEmailOrFallback(string repositoryRootFolder)
        {
            // 1) Repository-local config
            if (TryGetGitConfigValue(repositoryRootFolder, "user.email", useGlobal: false, out var localEmail) &&
                !localEmail.IsNullOrEmpty())
                return localEmail.Trim();

            // 2) Global config
            if (TryGetGitConfigValue(repositoryRootFolder, "user.email", useGlobal: true, out var globalEmail) &&
                !globalEmail.IsNullOrEmpty())
                return globalEmail.Trim();

            // 3) Fallback
            return $"{Environment.UserDomainName}\\{Environment.UserName}";
        }

        private static bool TryGetGitConfigValue(string repositoryRootFolder, string configKey, bool useGlobal, out string value)
        {
            value = string.Empty;

            var args = useGlobal
                ? $"config --global --get {EscapeGitArg(configKey)}"
                : $"config --get {EscapeGitArg(configKey)}";

            // Missing key returns non-zero exit code; do not log as an error.
            if (!RunGitCommand(repositoryRootFolder, args, out var output, logOnSuccess: false, logOnFailure: false))
                return false;

            value = output.Trim();
            return true;
        }


        public static async Task<bool> FetchAllAsync(string repoPath, CancellationToken cancellationToken = default)
        {
            var (ok, _) = await RunGitCommandAsync(
                    repoPath,
                    "fetch --all --prune",
                    timeout: TimeSpan.FromSeconds(60),
                    cancellationToken: cancellationToken,
                    logOnSuccess: false,
                    logOnFailure: true)
                .ConfigureAwait(false);

            return ok;
        }


        private static bool Checkout(string repoPath, string checkoutArgs)
        {
            try
            {
                string result;
                if (RunGitCommand(repoPath, $"checkout {checkoutArgs}", out result))
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
            return RunGitCommand(
                workingDirectory: repoPath,
                arguments: $"show-ref --verify --quiet refs/heads/{branchName}",
                combinedOutput: out _,
                logOnSuccess: false,
                logOnFailure: false);
        }

        private static bool RemoteBranchExists(string repoPath, string remoteName, string branchName)
        {
            // Check refs/remotes/origin/<branch>
            string result;
            return RunGitCommand(repoPath, $"show-ref --verify --quiet refs/remotes/{remoteName}/{branchName}", out result);
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

                if (RunGitCommand(Directory.GetParent(targetPath).FullName, $"clone {gitUrl} \"{targetPath}\"", out result))
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
                if (RunGitCommand(repoPath, "fetch --all"))
                {
                    if (RunGitCommand(repoPath, "status -sb", out result))
                    {
                        MessageLogger.Info($"Model {profileName}: {result}");
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
            if (configPath.IsNullOrEmpty() || !File.Exists(configPath))
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

        public static string ExtractAzureDevOpsProject(string gitUrl)
        {
            var match = Regex.Match(gitUrl, @"visualstudio\.com\/([^\/]+)\/_git\/");

            if (match.Success && match.Groups.Count > 1)
                return Uri.UnescapeDataString(match.Groups[1].Value);

            return string.Empty;
        }

        public static string ExtractAzureDevOpsRepo(string gitUrl)
        {
            var match = Regex.Match(gitUrl, @"_git\/([^\/]+)$");

            if (match.Success && match.Groups.Count > 1)
                return Uri.UnescapeDataString(match.Groups[1].Value);

            return string.Empty;
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
            if (url.IsNullOrEmpty())
                return url;

            var normalizedUrl = url;

            // SSH style: git@host:org/repo.git  -> https://host/org/repo
            if (normalizedUrl.StartsWith("git@", StringComparison.OrdinalIgnoreCase))
            {
                normalizedUrl = Regex.Replace(
                    normalizedUrl,
                    @"git@([^:]+):(.+?)\.git$",
                    "https://$1/$2",
                    RegexOptions.IgnoreCase);
            }

            // Strip user-info from https://user@host/...
            normalizedUrl = StripUrlUserInfo(normalizedUrl);

            return normalizedUrl;
        }

        private static string StripUrlUserInfo(string url)
        {
            if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
                return url;

            if (uri.UserInfo.IsNullOrEmpty())
                return url;

            var uriBuilder = new UriBuilder(uri)
            {
                UserName = string.Empty,
                Password = string.Empty
            };

            return uriBuilder.Uri.ToString();
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

        private static bool RunGitCommand(string workingDirectory, string arguments, bool logOnSuccess = false, bool logOnFailure = true)
        {
            var result = string.Empty;
            return RunGitCommand(workingDirectory, arguments, out result, logOnSuccess, logOnFailure);
        }

        private static bool RunGitCommand(string workingDirectory, string arguments, out string combinedOutput, bool logOnSuccess = false, bool logOnFailure = true)
        {
            var (ok, output) = RunGitCommandAsync(
                    workingDirectory,
                    arguments,
                    timeout: TimeSpan.FromSeconds(60),
                    cancellationToken: CancellationToken.None,
                    logOnSuccess: logOnSuccess,
                    logOnFailure: logOnFailure)
                .GetAwaiter()
                .GetResult();

            combinedOutput = output ?? string.Empty;
            return ok;
        }

        private static async Task<(bool Ok, string Output)> RunGitCommandAsync(string workingDirectory, string arguments, TimeSpan timeout, 
            CancellationToken cancellationToken, bool logOnSuccess = false, bool logOnFailure = true)
        {
            if (workingDirectory.IsNullOrEmpty())
                return (false, "Working directory is null or empty.");

            var processStartInfo = new ProcessStartInfo
            {
                FileName = "git",
                Arguments = arguments,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
                WorkingDirectory = workingDirectory
            };

            // Prevent Git from prompting for credentials in a non-interactive process.
            processStartInfo.Environment["GIT_TERMINAL_PROMPT"] = "0";
            processStartInfo.Environment["GCM_INTERACTIVE"] = "Never";

            try
            {
                using var process = new Process { StartInfo = processStartInfo, EnableRaisingEvents = true };

                if (!process.Start())
                    return (false, $"Failed to start git {arguments}");

                var standardOutputTask = process.StandardOutput.ReadToEndAsync();
                var standardErrorTask = process.StandardError.ReadToEndAsync();

                using var timeoutCancellationTokenSource = new CancellationTokenSource(timeout);
                using var linkedCancellationTokenSource = CancellationTokenSource.CreateLinkedTokenSource(
                    cancellationToken,
                    timeoutCancellationTokenSource.Token);

                try
                {
                    await process.WaitForExitAsync(linkedCancellationTokenSource.Token).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    try { process.Kill(entireProcessTree: true); } catch { }

                    var reason = cancellationToken.IsCancellationRequested ? "canceled" : "timed out";
                    if (logOnFailure)
                        MessageLogger.Error($"❌ Git command {reason}: git {arguments}");

                    return (false, $"Git command {reason}: git {arguments}");
                }

                var standardOutput = await standardOutputTask.ConfigureAwait(false) ?? string.Empty;
                var standardError = await standardErrorTask.ConfigureAwait(false) ?? string.Empty;

                var combinedOutput = (standardOutput + "\n" + standardError).Trim();

                if (process.ExitCode == 0)
                {
                    if (logOnSuccess && !combinedOutput.IsNullOrEmpty())
                        MessageLogger.Info(combinedOutput);

                    return (true, combinedOutput);
                }

                if (logOnFailure && !combinedOutput.IsNullOrEmpty())
                    MessageLogger.Error(combinedOutput);

                return (false, combinedOutput);
            }
            catch (Exception exception)
            {
                if (logOnFailure)
                    MessageLogger.Error($"❌ Git command failed: git {arguments}. {exception.Message}");

                return (false, exception.Message);
            }
        }

    }
}
