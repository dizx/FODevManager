using FODevManager.Models;
using FODevManager.Services;
using FODevManager.Utils;

namespace FODevManager.Operations;

public sealed class ModelOperations(WorkflowContext context)
{
    public object List(string profileName) => context.Profile(profileName).AllModels;
    public ProfileEnvironmentModel Show(string profileName, string modelName) => WorkflowContext.Model(context.Profile(profileName), modelName);

    public object Add(string profileName, string path, string modelName)
    {
        new ModelPathOperations(context).Add(profileName, path, modelName);
        return List(profileName);
    }

    public object Create(string profileName, string modelName)
    {
        var profile = context.Profile(profileName);
        WorkflowContext.ValidateName(modelName);
        if (profile.FindModel(modelName) != null) throw new InvalidOperationException("Model already exists");
        ModelPathSafety.RequireNewDestination(Path.Combine(context.Config.DefaultSourceDirectory, modelName));
        WorkflowContext.Require(context.Profiles.CreateModel(profileName, modelName), "Create model");
        return Show(profileName, modelName);
    }

    public static IReadOnlyList<ProfileEnvironmentModel> RemovalSet(ProfileModel profile, ProfileEnvironmentModel model)
    {
        if (model.ModelType != ModelType.CompiledNuget) return new[] { model };
        var repository = profile.FindRepositoryForModel(model);
        // Legacy standalone package members still form a coherent removal set
        var members = repository?.Models ?? profile.StandaloneModels;
        ArgumentException.ThrowIfNullOrWhiteSpace(model.PackageId);
        return members.Where(m => m.ModelType == ModelType.CompiledNuget && m.PackageId.SameAs(model.PackageId)).ToArray();
    }

    public object Remove(string profileName, string modelName, CancellationToken token)
    {
        var profile = context.Profile(profileName);
        var model = WorkflowContext.Model(profile, modelName);
        var members = RemovalSet(profile, model);
        if (model.ModelType == ModelType.CompiledNuget && profile.FindRepositoryForModel(model) is { } repo)
            return new PackageOperations(context).Remove(profileName, repo.RepoId, model.PackageId, token);
        new DeploymentOperations(context).UndeploySet(profile, members, token);
        profile = context.Profile(profileName);
        foreach (var member in members)
        {
            token.ThrowIfCancellationRequested();
            context.Solutions.RemoveProjectFromSolution(profile, member.ModelName);
            foreach (var repository in profile.Repositories) repository.Models.RemoveAll(m => m.ModelName.SameAs(member.ModelName));
            profile.StandaloneModels.RemoveAll(m => m.ModelName.SameAs(member.ModelName));
        }
        profile.Repositories.RemoveAll(r => r.Models.Count == 0);
        context.Files.SaveProfile(profile, updateExternal: true);
        return new { RemovedModels = members.Select(m => m.ModelName).ToArray() };
    }

    public object UpdateProperties(string profileName, string modelName, bool isMainFoModel)
    {
        var model = Show(profileName, modelName);
        if (model.ModelType != ModelType.Source) throw new InvalidOperationException("Only source models have an editable solution role");
        model.IsMainFOModel = isMainFoModel;
        context.Profiles.UpdateModelProperties(profileName, model);
        return Show(profileName, modelName);
    }

    public object Version(string profileName, string modelName)
    {
        var model = Show(profileName, modelName);
        var available = context.Versions.TryGetVersionText(model, out var version);
        return new { model.ModelName, Available = available, Version = version };
    }

    public object UpdateVersion(string profileName, string modelName, int major, int minor, int revision)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(major);
        ArgumentOutOfRangeException.ThrowIfNegative(minor);
        ArgumentOutOfRangeException.ThrowIfNegative(revision);
        var model = Show(profileName, modelName);
        if (model.ModelType != ModelType.Source) throw new InvalidOperationException("Version editing requires a source model; use nuget_update for package versions");
        WorkflowContext.Require(context.Versions.TryUpdateSourceVersion(model, new ModelVersion(major, minor, revision)), "Update source version");
        return Version(profileName, modelName);
    }
}
