namespace FODevManager.Models;

public sealed record AzureDevOpsProject(string Id, string Name, string? State, string? Visibility, string WebUrl);

public sealed record AzureDevOpsLocalRegistration(string ProfileName, string RepoId, string LocalPath);

public sealed record AzureDevOpsRepository(string Id, string Name, string ProjectId, string ProjectName,
    string? DefaultBranch, string CloneUrl, string WebUrl, bool IsDisabled)
{
    public IReadOnlyList<AzureDevOpsLocalRegistration> LocalRegistrations { get; init; } = [];
    public bool AlreadyRegistered => LocalRegistrations.Count != 0;
}

public sealed record AzureDevOpsProfileModelSummary(string ModelName, string ModelType, string? Repository,
    bool IsMainFOModel, string? PackageId, string? PackageVersion);

public sealed record AzureDevOpsProfileCandidate(string Path, bool IsValid, string? Format, string? ProfileName,
    IReadOnlyList<AzureDevOpsProfileModelSummary> Models, IReadOnlyList<string> ValidationErrors);

public sealed record AzureDevOpsRepositoryInspection(AzureDevOpsRepository Repository, string Status, string? Branch,
    string? CommitId, IReadOnlyList<AzureDevOpsProfileCandidate> ProfileDefinitions)
{
    public int ValidProfileCount => ProfileDefinitions.Count(p => p.IsValid);
}
