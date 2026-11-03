using FODevManager.Models;
using FODevManager.Services;
using FODevManager.Utils;

namespace FODevManager.Operations;

public static class DeploymentOwnership
{
    public static bool Verify(ProfileModel profile, ProfileEnvironmentModel model, AppConfig config,
        IDirectoryLinkService links, IDeploymentLedgerService ledger)
    {
        WorkflowContext.ValidateName(model.ModelName);
        var path = Path.Combine(config.DeploymentBasePath, model.ModelName);
        if (!links.Exists(path))
        {
            if (File.Exists(path) || new DirectoryInfo(path).LinkTarget != null)
                throw new InvalidOperationException($"Refusing to remove '{model.ModelName}': deployment path is a file or broken link");
            return false;
        }
        var target = links.ResolveLinkTarget(path);
        var source = model.ModelType == ModelType.Source ? model.MetadataFolder : model.CompiledModelFolder;
        var records = ledger.LoadRecords().Where(r => r.ModelName.SameAs(model.ModelName)).ToList();
        if (target == null || string.IsNullOrWhiteSpace(source) || records.Count != 1
            || records[0].IsUnmanaged || !records[0].ProfileName.SameAs(profile.ProfileName)
            || !SamePath(target, source) || !SamePath(records[0].SourcePath, source))
            throw new InvalidOperationException($"Refusing to remove '{model.ModelName}': deployment is not a verified link owned by profile '{profile.ProfileName}'");
        return true;
    }

    private static bool SamePath(string left, string right) =>
        !string.IsNullOrWhiteSpace(left) && !string.IsNullOrWhiteSpace(right)
        && string.Equals(Path.TrimEndingDirectorySeparator(Path.GetFullPath(left)),
            Path.TrimEndingDirectorySeparator(Path.GetFullPath(right)), StringComparison.OrdinalIgnoreCase);
}
