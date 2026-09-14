using FODevManager.Models;
using FODevManager.Utils;

namespace FODevManager.Operations;

public sealed class ModelPathOperations(WorkflowContext context)
{
    public void Add(string profileName, string path, string modelName)
    {
        var profile = context.Profile(profileName);
        WorkflowContext.ValidateName(modelName);
        path = ModelPathSafety.Normalize(path);
        if (!Directory.Exists(path)) throw new DirectoryNotFoundException("Model discovery path does not exist");
        if (ModelPathSafety.IsWithin(path, context.Config.DeploymentBasePath))
        {
            AddInstalled(profile, path, modelName);
            return;
        }

        ModelPathSafety.RejectLinkedAncestors(path);
        var discovered = Discover(path, modelName).ToArray();
        // Resolve and validate every candidate before services can save membership or edit a solution
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var candidate in discovered)
        {
            WorkflowContext.ValidateName(candidate.Name);
            if (!names.Add(candidate.Name) || profile.FindModel(candidate.Name) != null)
                throw new InvalidOperationException($"Model '{candidate.Name}' is already selected or registered");
            ModelPathSafety.RejectLinkedAncestors(candidate.Path);
            var root = FileHelper.GetModelRootFolder(candidate.Path);
            if (candidate.Type == ModelType.Source)
            {
                FileHelper.GetProjectFilePath(candidate.Name, root);
                WorkflowContext.Require(Directory.Exists(FileHelper.GetMetadataFolder(candidate.Name, root)), "Resolve source metadata");
            }
        }
        foreach (var candidate in discovered)
        {
            WorkflowContext.ServiceCall(() => context.Deployment.AddModelToProfileIfNotExists(profileName, candidate.Name, candidate.Path, candidate.Type));
            var saved = context.Profile(profileName);
            var model = WorkflowContext.Model(saved, candidate.Name);
            if (candidate.Type == ModelType.Source)
                WorkflowContext.ServiceCall(() => context.Solutions.AddProjectToSolution(saved, model));
        }
    }

    private void AddInstalled(ProfileModel profile, string path, string name)
    {
        if (!ModelPathSafety.SamePath(path, Path.Combine(context.Config.DeploymentBasePath, name)))
            throw new InvalidOperationException("Installed path must be the exact deployment child matching modelName");
        if (new DirectoryInfo(path).LinkTarget != null)
        {
            var model = profile.FindModel(name) ?? throw new InvalidOperationException("Cannot convert a foreign or unmanaged deployment link");
            WorkflowContext.Require(new DeploymentOperations(context).Verify(profile, model), "Verify existing managed model");
            return;
        }
        if (profile.FindModel(name) != null || context.Ledger.LoadRecords().Any(r => r.ModelName.SameAs(name)))
            throw new InvalidOperationException("Installed conversion requires an unregistered physical model, not a managed deployment");
        var destination = Path.Combine(context.Config.DefaultSourceDirectory, name);
        ModelPathSafety.ValidateConversion(path, destination, name);
        // The service repeats preflight and verifies the copied bytes before deleting the physical source
        ServiceHelper.RefreshW3SVCState();
        WorkflowContext.ServiceCall(() => WorkflowContext.Require(context.Deployment.ConvertInstalledModelToProjectModel(name, profile), "Convert installed model"));
        var saved = context.Profile(profile.ProfileName);
        WorkflowContext.ServiceCall(() => context.Solutions.AddProjectToSolution(saved, WorkflowContext.Model(saved, name)));
    }

    private static IEnumerable<Candidate> Discover(string path, string fallbackName)
    {
        var candidates = new List<Candidate>();
        var folderName = Path.GetFileName(path);
        var libs = folderName.SameAs("Libs") || folderName.SameAs("Lib") ? new[] { path }
            : new[] { Path.Combine(path, "Libs"), Path.Combine(path, "Lib") };
        foreach (var lib in libs.Where(Directory.Exists))
            foreach (var folder in Directory.EnumerateDirectories(lib))
                if (File.Exists(Path.Combine(folder, Path.GetFileName(folder) + ".xref")))
                    candidates.Add(new(Path.GetFileName(folder), folder, ModelType.Compiled));
        var metadata = folderName.SameAs("Metadata") ? path : Path.Combine(path, "Metadata");
        if (Directory.Exists(metadata))
            foreach (var folder in Directory.EnumerateDirectories(metadata))
                candidates.Add(new(Path.GetFileName(folder), folder, ModelType.Source));
        if (candidates.Count > 0) return candidates;
        if (File.Exists(Path.Combine(path, folderName + ".xref"))) return [new(folderName, path, ModelType.Compiled)];
        if (Directory.GetParent(path)?.Name.SameAs("Metadata") == true) return [new(folderName, path, ModelType.Source)];
        return [new(fallbackName, path, ModelType.Source)];
    }

    private sealed record Candidate(string Name, string Path, ModelType Type);
}
