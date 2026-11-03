using FODevManager.Utils;

namespace FODevManager.Operations;

public sealed class EnvironmentOperations(WorkflowContext context)
{
    public object SolutionPath(string profileName) => new { Path = context.Solutions.GetSolutionFilePath(context.Profile(profileName)) };

    public object EnsureSolution(string profileName, CancellationToken token)
    {
        var profile = context.Profile(profileName);
        var path = context.Solutions.CreateSolutionFile(profile);
        WorkflowContext.Require(File.Exists(path), "Create solution");
        foreach (var model in profile.AllModels.Where(m => m.ModelType == Models.ModelType.Source))
        {
            token.ThrowIfCancellationRequested();
            context.Solutions.AddProjectToSolution(profile, model);
        }
        return new { ArtifactPaths = new[] { path } };
    }

    public object OpenSolution(string profileName)
    {
        var path = context.Solutions.GetSolutionFilePath(context.Profile(profileName));
        WorkflowContext.Require(File.Exists(path), "Locate solution");
        context.Solutions.OpenSolution(path);
        return new { Path = path };
    }

    public object Database(string profileName) => new { context.Profile(profileName).DatabaseName };

    public object SetDatabase(string profileName, string databaseName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(databaseName);
        if (context.Profile(profileName).IsActive) ServiceHelper.RefreshW3SVCState();
        context.Profiles.SetDatabaseName(profileName, databaseName);
        return Database(profileName);
    }

    public object ApplyDatabase(string profileName)
    {
        context.Profile(profileName);
        ServiceHelper.RefreshW3SVCState();
        context.Profiles.ApplyDatabase(profileName);
        return Database(profileName);
    }
}
