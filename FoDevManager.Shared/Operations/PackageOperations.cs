using FODevManager.Models;
using FODevManager.Utils;
using FODevManager.Services;
using System.Text.Json;

namespace FODevManager.Operations;

public sealed class PackageOperations(WorkflowContext context)
{
    public object Versions(string profileName, string repoId, string packageId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(packageId);
        return context.Packages.GetAvailablePackageVersions(WorkflowContext.Repository(context.Profile(profileName), repoId), packageId, throwOnFailure: true);
    }

    public object Add(string profileName, string repoId, string packageUrl, CancellationToken token)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(packageUrl);
        WorkflowContext.Require(DeployablePackageService.TryParseNugetPackageUrl(packageUrl, out var packageId, out var version), "Parse NuGet package URL");
        return Synchronize(profileName, repoId, token, (profile, repo) =>
        {
            context.Packages.AddOrUpdatePackageFromUrl(profile, repo, packageUrl);
            WorkflowContext.Require(repo.Models.Any(m => m.PackageId.SameAs(packageId) && m.PackageVersion.SameAs(version)), "Discover requested package models");
        });
    }

    public object Update(string profileName, string repoId, string packageId, string version, CancellationToken token)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(version);
        RequirePackage(profileName, repoId, packageId);
        return Synchronize(profileName, repoId, token, (profile, repo) =>
        {
            context.Packages.UpdatePackageVersion(profile, repo, packageId, version);
            WorkflowContext.Require(repo.Models.Any(m => m.PackageId.SameAs(packageId) && m.PackageVersion.SameAs(version)), "Update package version");
            foreach (var model in repo.Models.Where(m => m.PackageId.SameAs(packageId)))
                model.PackageUrl = ProfileService.UpdatePackageOverviewUrl(model.PackageUrl, packageId, version);
        });
    }

    public object Remove(string profileName, string repoId, string packageId, CancellationToken token)
    {
        RequirePackage(profileName, repoId, packageId);
        return Synchronize(profileName, repoId, token, (profile, repo) =>
        {
            WorkflowContext.Require(context.Packages.RemovePackage(profile, repo, packageId), "Remove package");
            WorkflowContext.Require(!repo.Models.Any(m => m.ModelType == ModelType.CompiledNuget && m.PackageId.SameAs(packageId)), "Verify package removal");
        });
    }

    private void RequirePackage(string profileName, string repoId, string packageId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(packageId);
        var repo = WorkflowContext.Repository(context.Profile(profileName), repoId);
        WorkflowContext.Require(context.Packages.GetConfiguredPackageVersions(repo).ContainsKey(packageId)
            || repo.Models.Any(m => m.ModelType == ModelType.CompiledNuget && m.PackageId.SameAs(packageId)), "Locate package in selected repository");
    }

    public object Prepare(string profileName, CancellationToken token)
    {
        var profile = context.Profile(profileName);
        // Validate all repositories before the first restore can change membership
        var deployment = new DeploymentOperations(context);
        foreach (var model in profile.StandaloneModels.Where(m => m.ModelType == ModelType.CompiledNuget))
            WorkflowContext.Require(Directory.Exists(model.CompiledModelFolder), "Locate standalone NuGet payload; repository membership is required for feed restore");
        foreach (var model in profile.AllModels.Where(m => m.ModelType == ModelType.CompiledNuget)) deployment.Verify(profile, model);

        // Resolve every repository on a detached graph before disturbing any healthy deployment
        // WorkflowContext disables the package service's independent legacy-link cleanup
        var desired = FileService.SerializedClone(profile);
        foreach (var repo in desired.Repositories)
        {
            token.ThrowIfCancellationRequested();
            WorkflowContext.ServiceCall(() => context.Packages.EnsureCompiledNugetModels(desired, repo));
            foreach (var package in context.Packages.GetConfiguredPackageVersions(repo))
            {
                if (desired.AllModels.Any(m => m.ModelType != ModelType.CompiledNuget && m.ModelName.SameAs(package.Key))) continue;
                var members = repo.Models.Where(m => m.ModelType == ModelType.CompiledNuget && m.PackageId.SameAs(package.Key)).ToArray();
                WorkflowContext.Require(members.Length > 0 && members.All(m => m.PackageVersion.SameAs(package.Value)
                    && Directory.Exists(m.CompiledModelFolder)
                    && Directory.EnumerateFiles(m.CompiledModelFolder, "*.xref", SearchOption.AllDirectories).Any()),
                    "Verify resolved package " + package.Key);
            }
        }
        token.ThrowIfCancellationRequested();
        WorkflowContext.Require(desired.AllModels.GroupBy(m => m.ModelName, StringComparer.OrdinalIgnoreCase).All(g => g.Count() == 1),
            "Resolve unique model membership");
        if (JsonSerializer.Serialize(profile) == JsonSerializer.Serialize(desired)) return profile.AllModels;

        var affected = profile.AllModels.Where(old =>
        {
            if (old.ModelType != ModelType.CompiledNuget) return false;
            var next = desired.FindModel(old.ModelName);
            return next == null || !old.CompiledModelFolder.SameAs(next.CompiledModelFolder)
                || !old.PackageId.SameAs(next.PackageId) || !old.PackageVersion.SameAs(next.PackageVersion);
        }).ToArray();
        var deployedNames = affected.Where(m => deployment.Verify(profile, m)).Select(m => m.ModelName).ToArray();
        foreach (var added in desired.AllModels.Where(m => profile.FindModel(m.ModelName) == null))
        {
            deployment.Verify(desired, added);
            added.IsDeployed = false;
        }
        if (affected.Length > 0) deployment.UndeploySet(profile, affected, token);
        foreach (var old in affected)
            if (desired.FindModel(old.ModelName) is { } next) next.IsDeployed = false;
        context.Files.SaveProfile(desired, updateExternal: false);
        foreach (var name in deployedNames)
            if (desired.FindModel(name) != null) deployment.DeployPrepared(profileName, name, token);
        return context.Profile(profileName).AllModels;
    }

    private object Synchronize(string profileName, string repoId, CancellationToken token, Action<ProfileModel, RepositoryModel> action, bool updateExternal = true)
    {
        var profile = context.Profile(profileName);
        var repo = WorkflowContext.Repository(profile, repoId);
        var deployment = new DeploymentOperations(context);
        // EnsureCompiledNugetModels reconciles every package in the repository
        var members = repo.Models.Where(m => m.ModelType == ModelType.CompiledNuget).ToArray();
        var deployedNames = members.Where(m => deployment.Verify(profile, m)).Select(m => m.ModelName).ToArray();
        deployment.UndeploySet(profile, members, token);
        token.ThrowIfCancellationRequested();
        profile = context.Profile(profileName);
        repo = WorkflowContext.Repository(profile, repoId);
        var before = JsonSerializer.Serialize(profile);
        WorkflowContext.ServiceCall(() => action(profile, repo));
        foreach (var package in context.Packages.GetConfiguredPackageVersions(repo))
        {
            if (profile.AllModels.Any(m => m.ModelType != ModelType.CompiledNuget && m.ModelName.SameAs(package.Key))) continue;
            WorkflowContext.Require(repo.Models.Any(m => m.ModelType == ModelType.CompiledNuget && m.PackageId.SameAs(package.Key)
                && m.PackageVersion.SameAs(package.Value) && Directory.Exists(m.CompiledModelFolder)), "Verify restored package " + package.Key);
        }
        var changed = before != JsonSerializer.Serialize(profile);
        if (changed) context.Files.SaveProfile(profile, updateExternal: updateExternal);
        foreach (var name in deployedNames)
        {
            token.ThrowIfCancellationRequested();
            if (profile.FindModel(name) != null) deployment.DeployPrepared(profileName, name, token);
        }
        return new { Repository = repo, PreviouslyDeployed = deployedNames, MembershipChanged = changed };
    }

    public object Build(string profileName, string modelName, bool publish, CancellationToken token)
    {
        var profile = context.Profile(profileName);
        var model = WorkflowContext.Model(profile, modelName);
        if (model.ModelType == ModelType.CompiledNuget) throw new InvalidOperationException("Use the original NuGet package for NuGet-backed models");
        token.ThrowIfCancellationRequested();
        var previous = context.Config.PushDeployablePackageOnBuild;
        try
        {
            context.Config.PushDeployablePackageOnBuild = publish;
            WorkflowContext.Require(context.Profiles.BuildDeployableNugetPackage(profileName, modelName), "Build package");
            var artifacts = context.Packages.LastBuiltArtifactPaths;
            WorkflowContext.Require(artifacts.Count > 0 && artifacts.All(File.Exists), "Locate generated package artifacts");
            return new { ArtifactPaths = artifacts, Published = publish };
        }
        finally { context.Config.PushDeployablePackageOnBuild = previous; }
    }
}
