using System.ComponentModel;
using FODevManager.Operations;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace FODevManager.Mcp;

[McpServerToolType]
public sealed class AzureDevOpsTools(ToolExecution execution)
{
    [McpServerTool(Name = "azure_devops_projects_list", ReadOnly = true), Description("List accessible Azure DevOps Services projects with pagination. organization is a name such as peritus-no, not a URL. Uses configured AzureArtifactsPat with project-read permission; does not clone or import")]
    public Task<CallToolResult> Projects(string organization, IProgress<ProgressNotificationValue> progress, CancellationToken token, string? requestId = null) =>
        execution.RunAsync("azure_devops_projects_list", false, async (c, ct) => await new AzureDevOpsOperations(c).ProjectsAsync(organization, ct), progress, token, requestId);

    [McpServerTool(Name = "azure_devops_repositories_list", ReadOnly = true), Description("List accessible Azure DevOps repositories for an organization name, optionally filtered by project name or ID. Returns Azure repository GUIDs, clone URLs and matching saved local profiles. Uses configured AzureArtifactsPat with Code: Read; no clone, fetch, import or profile mutation")]
    public Task<CallToolResult> Repositories(string organization, IProgress<ProgressNotificationValue> progress, CancellationToken token, string? project = null, string? requestId = null) =>
        execution.RunAsync("azure_devops_repositories_list", false, async (c, ct) => await new AzureDevOpsOperations(c).RepositoriesAsync(organization, project, ct), progress, token, requestId);

    [McpServerTool(Name = "azure_devops_repository_inspect", ReadOnly = true), Description("Inspect Artifact/Artifacts profile JSON in an Azure repository GUID at a supplied branch or default branch, pinned to its commit. Return all candidates, structural validation and model summaries without cloning/importing. Uses configured AzureArtifactsPat with Code: Read; structural validity does not guarantee an import will succeed or select that candidate")]
    public Task<CallToolResult> Inspect(string organization, string repositoryId, IProgress<ProgressNotificationValue> progress, CancellationToken token, string? branch = null, string? requestId = null) =>
        execution.RunAsync("azure_devops_repository_inspect", false, async (c, ct) => await new AzureDevOpsOperations(c).InspectAsync(organization, repositoryId, branch, ct), progress, token, requestId);
}
