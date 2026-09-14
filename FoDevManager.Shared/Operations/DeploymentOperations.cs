using FODevManager.Models;
using FODevManager.Utils;

namespace FODevManager.Operations;

public sealed class DeploymentOperations(WorkflowContext context)
{
    public object Inspect(string profileName, string? modelName = null)
    {
        var profile = context.Profile(profileName);
        return Select(profile, modelName).Select(model =>
        {
            var path = Path.Combine(context.Config.DeploymentBasePath, model.ModelName);
            string? reason = null;
            bool owned = false;
            try { owned = Verify(profile, model); }
            catch (InvalidOperationException ex) { reason = ex.Message; }
            return new { model.ModelName, Path = path, Exists = context.Links.Exists(path),
                Target = context.Links.Exists(path) ? context.Links.ResolveLinkTarget(path) : new DirectoryInfo(path).LinkTarget,
                VerifiedOwned = owned, Reason = reason,
                Records = context.Ledger.LoadRecords().Where(r => r.ModelName.SameAs(model.ModelName)).ToArray() };
        }).ToArray();
    }

    public bool Verify(ProfileModel profile, ProfileEnvironmentModel model) =>
        DeploymentOwnership.Verify(profile, model, context.Config, context.Links, context.Ledger);

    public void UndeploySet(ProfileModel profile, IReadOnlyList<ProfileEnvironmentModel> models, CancellationToken token)
    {
        var owned = models.Where(m => Verify(profile, m)).ToArray();
        token.ThrowIfCancellationRequested();
        if (owned.Length > 0) context.PrepareServiceControl();
        foreach (var model in owned)
        {
            token.ThrowIfCancellationRequested();
            WorkflowContext.ServiceCall(() => context.Deployment.UnDeployModel(profile.ProfileName, model.ModelName));
            WorkflowContext.Require(!context.Links.Exists(Path.Combine(context.Config.DeploymentBasePath, model.ModelName)), "Undeploy");
        }
        var saved = context.Profile(profile.ProfileName);
        var changed = false;
        foreach (var model in models)
        {
            var entry = WorkflowContext.Model(saved, model.ModelName);
            if (entry.IsDeployed) { entry.IsDeployed = false; changed = true; }
            var records = context.Ledger.LoadRecords().Where(r => r.ModelName.SameAs(model.ModelName)).ToArray();
            if (records.Length == 1 && !records[0].IsUnmanaged && records[0].ProfileName.SameAs(profile.ProfileName))
                context.Ledger.RemoveDeployment(model.ModelName);
        }
        if (changed) context.Files.SaveProfile(saved);
    }

    public object Undeploy(string profileName, string? modelName, CancellationToken token)
    {
        var profile = context.Profile(profileName);
        UndeploySet(profile, Select(profile, modelName), token);
        return Inspect(profileName, modelName);
    }

    public object UndeployAll(CancellationToken token)
    {
        var owned = context.Ledger.LoadRecords().Where(r => !r.IsUnmanaged).Select(record =>
        {
            var profile = context.Profile(record.ProfileName);
            var model = WorkflowContext.Model(profile, record.ModelName);
            Verify(profile, model);
            return (Profile: profile, Model: model);
        }).ToArray();
        foreach (var group in owned.GroupBy(item => item.Profile.ProfileName))
            UndeploySet(group.First().Profile, group.Select(item => item.Model).ToArray(), token);
        return new { Profiles = owned.Select(item => item.Profile.ProfileName).Distinct().ToArray() };
    }

    public object Deploy(string profileName, string? modelName, CancellationToken token)
    {
        var profile = context.Profile(profileName);
        if (Select(profile, modelName).Any(m => m.ModelType == ModelType.CompiledNuget))
            new PackageOperations(context).Prepare(profileName, token);
        return DeployPrepared(profileName, modelName, token);
    }

    internal object DeployPrepared(string profileName, string? modelName, CancellationToken token)
    {
        var profile = context.Profile(profileName);
        var models = Select(profile, modelName);
        foreach (var model in models) Verify(profile, model);
        foreach (var model in models)
        {
            token.ThrowIfCancellationRequested();
            context.PrepareServiceControl();
            WorkflowContext.ServiceCall(() => WorkflowContext.Require(context.Deployment.DeployModel(profileName, model.ModelName, preparePackage: false), "Deploy model"));
            var saved = context.Profile(profileName);
            WorkflowContext.Require(Verify(saved, WorkflowContext.Model(saved, model.ModelName)), "Verify deployment");
        }
        return Inspect(profileName, modelName);
    }

    private static List<ProfileEnvironmentModel> Select(ProfileModel profile, string? modelName) =>
        modelName is null ? profile.AllModels : [WorkflowContext.Model(profile, modelName)];
}
