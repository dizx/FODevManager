using FODevManager.Models;
using FODevManager.Services;

namespace FODevManager.Operations;

public sealed class AzureDevOpsOperations(WorkflowContext context, AzureDevOpsService? service = null)
{
    private readonly AzureDevOpsService azure = service ?? new AzureDevOpsService(context.Config);

    public Task<IReadOnlyList<AzureDevOpsProject>> ProjectsAsync(string organization, CancellationToken token) => azure.ListProjectsAsync(organization, token);

    public async Task<IReadOnlyList<AzureDevOpsRepository>> RepositoriesAsync(string organization, string? project, CancellationToken token)
    {
        var repositories = await azure.ListRepositoriesAsync(organization, project, token).ConfigureAwait(false);
        var profiles = new FileService(context.Config, ensureDirectory: false).GetAllProfiles();
        return repositories.Select(repository => WithLocalMatches(organization, repository, profiles)).ToArray();
    }

    public async Task<AzureDevOpsRepositoryInspection> InspectAsync(string organization, string repositoryId, string? branch, CancellationToken token)
    {
        var inspection = await azure.InspectRepositoryAsync(organization, repositoryId, branch, token).ConfigureAwait(false);
        var profiles = new FileService(context.Config, ensureDirectory: false).GetAllProfiles();
        return inspection with { Repository = WithLocalMatches(organization, inspection.Repository, profiles) };
    }

    private static AzureDevOpsRepository WithLocalMatches(string organization, AzureDevOpsRepository repository, IReadOnlyList<ProfileModel> profiles) =>
        repository with { LocalRegistrations = profiles.SelectMany(profile => (profile.Repositories ?? [])
            .Where(saved => Matches(saved.GitUrl, organization, repository))
            .Select(saved => new AzureDevOpsLocalRegistration(profile.ProfileName, saved.RepoId, saved.RepoRootFolder)))
            .OrderBy(m => m.ProfileName, StringComparer.OrdinalIgnoreCase).ThenBy(m => m.RepoId, StringComparer.OrdinalIgnoreCase).ToArray() };

    private static bool Matches(string? url, string organization, AzureDevOpsRepository repository)
    {
        if (string.IsNullOrWhiteSpace(url)) return false;
        url = url.Trim();
        const string sshPrefix = "git@ssh.dev.azure.com:";
        if (url.StartsWith(sshPrefix, StringComparison.OrdinalIgnoreCase)) url = "ssh://git@ssh.dev.azure.com/" + url[sshPrefix.Length..];
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri)) return false;
        var parts = uri.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries).Select(Uri.UnescapeDataString).ToList();
        string org;
        if (uri.Host.Equals("dev.azure.com", StringComparison.OrdinalIgnoreCase) && uri.Scheme == "https" && parts.Count is 3 or 4)
        { org = parts[0]; parts.RemoveAt(0); if (!RemoveGitSegment(parts)) return false; }
        else if (uri.Host.Equals("ssh.dev.azure.com", StringComparison.OrdinalIgnoreCase) && uri.Scheme == "ssh" && parts.Count == 4 && parts[0] == "v3")
        { org = parts[1]; parts.RemoveRange(0, 2); }
        else if (uri.Host.EndsWith(".visualstudio.com", StringComparison.OrdinalIgnoreCase) && uri.Scheme == "https")
        {
            org = uri.Host[..^".visualstudio.com".Length];
            if (parts.Count is 3 or 4 && parts[0].Equals("DefaultCollection", StringComparison.OrdinalIgnoreCase)) parts.RemoveAt(0);
            if (!RemoveGitSegment(parts)) return false;
        }
        else return false;
        var rawName = parts[^1];
        var name = rawName.EndsWith(".git", StringComparison.OrdinalIgnoreCase) ? rawName[..^4] : rawName;
        var matchesId = repository.Id.Equals(name, StringComparison.OrdinalIgnoreCase);
        var matchesName = repository.Name.Equals(name, StringComparison.OrdinalIgnoreCase) || repository.Name.Equals(rawName, StringComparison.OrdinalIgnoreCase);
        // A project-omitted name addresses the project's default same-named repository, not arbitrary same-named repos
        var matchesProject = parts.Count == 1
            ? matchesId || repository.ProjectName.Equals(repository.Name, StringComparison.OrdinalIgnoreCase)
            : repository.ProjectName.Equals(parts[0], StringComparison.OrdinalIgnoreCase) || repository.ProjectId.Equals(parts[0], StringComparison.OrdinalIgnoreCase);
        return organization.Equals(org, StringComparison.OrdinalIgnoreCase)
            && matchesProject && (matchesName || matchesId);
    }

    private static bool RemoveGitSegment(List<string> parts)
    {
        if (parts.Count is not (2 or 3) || !parts[^2].Equals("_git", StringComparison.OrdinalIgnoreCase)) return false;
        parts.RemoveAt(parts.Count - 2);
        return true;
    }
}
