using System.Text.Json;
using FODevManager.Models;
using FODevManager.Shared.Utils;
using FODevManager.Shared.Models;
using FODevManager.Utils;
using FODevManager.Messages;

namespace FODevManager.Operations;

public sealed class ProfileOperations(WorkflowContext context)
{
    public IReadOnlyList<string> List() => context.Files.GetAllProfileNames();
    public ProfileModel Show(string name) => context.Profile(name);
    public object Active() => new { Profiles = context.Files.GetAllProfiles().Where(p => p.IsActive).Select(p => p.ProfileName).ToArray() };

    public ProfileModel Create(string name)
    {
        WorkflowContext.ValidateName(name);
        if (context.Files.ExistProfile(name)) throw new InvalidOperationException("Profile already exists");
        context.Profiles.CreateProfile(name);
        return Show(name);
    }

    public object Delete(string name, CancellationToken token)
    {
        var profile = Show(name);
        new DeploymentOperations(context).UndeploySet(profile, profile.AllModels, token);
        token.ThrowIfCancellationRequested();
        context.Files.DeleteProfile(name);
        return new { DeletedProfile = name };
    }

    public ProfileModel ImportFile(string path, string? targetName, CancellationToken token = default)
    {
        token.ThrowIfCancellationRequested();
        using var document = JsonDocument.Parse(File.ReadAllText(path));
        var definition = document.RootElement.TryGetProperty("ExportFormatVersion", out _) || document.RootElement.TryGetProperty("ExportProfileVersion", out _)
            ? ExportProfileMapper.FromExport(document.RootElement.Deserialize<ExportProfileModel>() ?? throw new InvalidOperationException("Invalid export"), path)
            : document.RootElement.Deserialize<ProfileModel>() ?? throw new InvalidOperationException("Invalid profile");
        var name = targetName ?? definition.ProfileName;
        WorkflowContext.ValidateName(name);
        foreach (var model in definition.AllModels) WorkflowContext.ValidateName(model.ModelName);
        if (context.Files.ExistProfile(name))
        {
            var existing = Show(name);
            new DeploymentOperations(context).UndeploySet(existing, existing.AllModels, token);
        }
        token.ThrowIfCancellationRequested();
        var imported = context.Profiles.ImportProfile(path, name) ?? throw new InvalidOperationException("Import failed");
        return Show(imported.ProfileName);
    }

    public ProfileModel ImportRepository(string url, CancellationToken token = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(url);
        if (url.StartsWith('-') || url.Any(c => char.IsWhiteSpace(c) || c == '"')) throw new ArgumentException("Repository URL contains unsupported characters");
        WorkflowContext.ValidateName(GitHelper.ExtractAzureDevOpsRepo(url));
        var imported = context.Profiles.ImportProfileFromRepoUrl(url, path => ImportFile(path, null, token)) ?? throw new InvalidOperationException("Repository import failed");
        return Show(imported.ProfileName);
    }

    public object Export(string name, string path)
    {
        var profile = Show(name);
        var fullPath = Path.GetFullPath(path);
        using var stream = new FileStream(fullPath, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        JsonSerializer.Serialize(stream, ExportProfileMapper.ToExport(profile), new JsonSerializerOptions { WriteIndented = true });
        return new { ArtifactPaths = new[] { fullPath } };
    }

    public ProfileModel Refresh(string name)
    {
        var profile = Show(name);
        var deployment = new DeploymentOperations(context);
        // Refresh observes the saved membership; package reconciliation is an explicit nuget_prepare operation
        foreach (var model in profile.AllModels)
        {
            try { model.IsDeployed = deployment.Verify(profile, model); }
            catch (InvalidOperationException ex)
            {
                model.IsDeployed = false;
                MessageLogger.Warning(ex.Message);
            }
        }
        context.Profiles.UpdateGitBranchesInProfile(profile);
        return Show(name);
    }

    public ProfileModel Switch(string name, CancellationToken token)
    {
        var target = Show(name);
        if (target.IsActive) return target;
        if (context.Config.CheckUncommittedBeforeSwitch)
            foreach (var profile in context.Files.GetAllProfiles().Where(p => p.IsActive))
                foreach (var repo in profile.Repositories)
                    WorkflowContext.Require(!GitHelper.IsWorkingTreeDirtyStrict(repo.RepoRootFolder, token), "Check uncommitted changes before switch");
        foreach (var repo in target.Repositories.Where(r => r.AutoCheckoutOnProfileLoad && !string.IsNullOrWhiteSpace(r.PreferredBranch)))
            GitHelper.IsWorkingTreeDirtyStrict(repo.RepoRootFolder, token);
        token.ThrowIfCancellationRequested();
        new DeploymentOperations(context).UndeployAll(token);
        foreach (var repo in target.Repositories.Where(r => r.AutoCheckoutOnProfileLoad && !string.IsNullOrWhiteSpace(r.PreferredBranch)))
        {
            token.ThrowIfCancellationRequested();
            WorkflowContext.Require(GitHelper.ChangeBranch(repo.RepoRootFolder, repo.PreferredBranch!, repo.AutoStashOnDirtyCheckout,
                $"FO Dev Manager: profile '{name}' switch", createIfMissing: true), "Checkout preferred branch");
        }
        new PackageOperations(context).Prepare(name, token);
        new DeploymentOperations(context).DeployPrepared(name, null, token);
        token.ThrowIfCancellationRequested();
        WorkflowContext.ServiceCall(() => new EnvironmentOperations(context).ApplyDatabase(name));
        context.Profiles.SetActiveProfile(name);
        context.Profiles.UpdateGitBranchesInProfile(Show(name));
        return Show(name);
    }

    public async Task<object> CheckSync(string name) => await context.Profiles.CheckProfileModelChangesAsync(Show(name));

    public async Task<object> DismissSync(string name, string revision)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(revision);
        var profile = Show(name);
        var current = await context.Profiles.CheckProfileModelChangesAsync(profile);
        if (!string.Equals(current.DefinitionRevision, revision, StringComparison.Ordinal))
            throw new InvalidOperationException("Linked definition revision changed; check again before dismissing");
        context.Profiles.DismissProfileChanges(profile, revision);
        return new { Revision = revision };
    }

    public ProfileModel Reimport(string name, CancellationToken token)
    {
        var profile = Show(name);
        if (string.IsNullOrWhiteSpace(profile.ProfileFilePath)) throw new InvalidOperationException("Profile has no linked definition");
        var imported = ImportFile(profile.ProfileFilePath, name, token);
        imported.DatabaseName = profile.DatabaseName;
        context.Files.SaveProfile(imported);
        return imported;
    }
}
