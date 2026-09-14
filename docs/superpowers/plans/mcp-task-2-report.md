# Task 2 implementation report

## Scope and implementation

Implemented the headless `fodev.exe mcp` entry point on `feature/mcp-stdio`, using the official stable NuGet package **ModelContextProtocol 2.2.0**. The NuGet version index, tagged SDK documentation/source, and installed XML API documentation were consulted. Actual SDK APIs used include `AddMcpServer`, `WithStdioServerTransport`, explicit `WithTools<T>` registrations, `McpServerToolAttribute`, injected `IProgress<ProgressNotificationValue>`, and `CallToolResult` with both structured content and `IsError`.

The host registers **52 named, typed tools** in six domain groups. There is no CLI dispatcher, UI automation, custom reflection invocation, or placeholder tool implementation. Task 1's runner/result/redactor APIs are consumed unchanged.

Desktop inventory was taken from MainWindow handlers, background repository-health/profile-sync flows, repository/model property dialogs, SettingsViewModel, and their shared service implementations. Selection, expansion, window chrome and picker presentation map to explicit tool parameters or saved/live data responses.

## Exact tool / operation / desktop inventory

All operation classes below are under `FoDevManager.Shared/Operations`. Tool classes are under `FODevManager/Mcp`. Except `operation_status`, every tool executes through the shared runner; optional `requestId` is available for correlation. The SDK supplies progress and cancellation parameters, which are excluded from tool input schemas.

| Tool | Shared operation | Desktop equivalent / behavior |
| --- | --- | --- |
| `profiles_list` | `ProfileOperations.List` | Profile dropdown, fresh saved names |
| `profile_show` | `ProfileOperations.Show` | Selected profile information, saved state |
| `profile_active` | `ProfileOperations.Active` | Active environment indicator |
| `profile_create` | `ProfileOperations.Create` | Create profile, solution and active flag |
| `profile_delete` | `ProfileOperations.Delete` | Delete profile after whole-set owned-link validation |
| `profile_import_file` | `ProfileOperations.ImportFile` | Import picker, explicit path and optional local name |
| `profile_import_repository` | `ProfileOperations.ImportRepository` | Repository URL import and clone |
| `profile_export` | `ProfileOperations.Export` | Portable profile export, explicit new output path |
| `profile_refresh` | `ProfileOperations.Refresh` | Refresh local ownership/Git state while preserving membership and paths; explicit `nuget_prepare` performs package reconciliation |
| `profile_switch` | `ProfileOperations.Switch` | Dirty check, verified undeployment, preferred branches/stashes, package preparation, deployment, database, activation |
| `profile_sync_check` | `ProfileOperations.CheckSync` | Linked-definition monitoring; can bootstrap export |
| `profile_sync_dismiss` | `ProfileOperations.DismissSync` | Dismiss only the current explicit revision |
| `profile_sync_reimport` | `ProfileOperations.Reimport` | Reimport linked definition, preserving local name and database including AXDB |
| `models_list` | `ModelOperations.List` | Model list across repository and standalone membership |
| `model_show` | `ModelOperations.Show` | Model information/properties, saved state |
| `models_add_path` | `ModelOperations.Add` / `ModelPathOperations.Add` | Whole-collection preflight, source/compiled discovery and verified physical installed-model conversion |
| `model_create` | `ModelOperations.Create` | Create source model/project only in a new filesystem destination |
| `model_remove` | `ModelOperations.Remove` | Remove model; expands NuGet package membership |
| `model_properties_update` | `ModelOperations.UpdateProperties` | Main FO source-model solution role |
| `model_version` | `ModelOperations.Version` | Source/compiled/NuGet version display |
| `model_version_update` | `ModelOperations.UpdateVersion` | Non-negative source version editor |
| `repositories_list` | `RepositoryOperations.List` | Repository grouping, saved state |
| `repository_show` | `RepositoryOperations.Show` | Repository property information |
| `repository_properties_update` | `RepositoryOperations.Update` | Display name, preferred branch, auto checkout/stash, task and comment |
| `repository_status` | `RepositoryOperations.Status` | Live branch, dirty state, upstream health, HEAD and local main comparison; no implicit fetch |
| `git_fetch` | `RepositoryOperations.Fetch` | Fetch selected repository or entire profile |
| `repository_remote` | `RepositoryOperations.Remote` | Remote URL data and explicit browser-open choice |
| `git_assign_task` | `RepositoryOperations.AssignTask` | Task assignment with explicit switch, branch name, stash and branch creation choices |
| `repository_task_url` | `RepositoryOperations.TaskUrl` | Task URL data and explicit browser-open choice |
| `git_merge_main` | `RepositoryOperations.MergeMain` | Merge main/current branch update |
| `git_reset_profile` | `RepositoryOperations.Reset` | Stash, checkout main, fetch/pull across profile |
| `git_tag_release` | `RepositoryOperations.Release` | Revision bump, descriptor commit, release tag creation and tag push |
| `nuget_versions` | `PackageOperations.Versions` | Package-version dropdown/feed query |
| `nuget_add` | `PackageOperations.Add` | Repository picker replaced by required repoId; add URL and restore |
| `nuget_update` | `PackageOperations.Update` | Package-version change, package URLs and membership reconciliation |
| `nuget_remove` | `PackageOperations.Remove` | Remove reference and all package members coherently |
| `nuget_prepare` | `PackageOperations.Prepare` | Background compiled-package restore/preparation |
| `package_build` | `PackageOperations.Build` | Source build or compiled packaging, explicit required publish boolean, actual artifact paths |
| `deployment_inspect` | `DeploymentOperations.Inspect` | Live targets, ledger and verified ownership, no self-heal |
| `deployment_deploy` | `DeploymentOperations.Deploy` | Deploy model or profile, package preparation and verified final state |
| `deployment_undeploy` | `DeploymentOperations.Undeploy` | Undeploy selected model or profile |
| `deployment_undeploy_all` | `DeploymentOperations.UndeployAll` | Undeploy managed records across their actual owning profiles |
| `solution_path` | `EnvironmentOperations.SolutionPath` | Resolved solution path |
| `solution_ensure` | `EnvironmentOperations.EnsureSolution` | Create/update solution and add all source projects |
| `solution_open` | `EnvironmentOperations.OpenSolution` | Open Visual Studio |
| `database_get` | `EnvironmentOperations.Database` | Database display |
| `database_set` | `EnvironmentOperations.SetDatabase` | Database edit/apply; active-profile application behavior |
| `database_apply` | `EnvironmentOperations.ApplyDatabase` | Apply database to local environment |
| `application_about` | Application tool data | Application version/about and execution semantics |
| `settings_read` | `SettingsOperations.Read` | Effective non-secret settings |
| `settings_update` | `SettingsOperations.Update` | Typed desktop settings plus existing supported path/task/model-ID configuration |
| `operation_status` | Runner history, independent of execution gate | Running/queued/completed operation lookup by operation ID or caller request ID |

## Execution, redaction and configuration

- Early dispatch occurs before CLI parsing or ConsoleSubscriber/SerilogSubscriber registration
- MCP sets the Console engine and installs its own independently redacted MessageBus stderr/file subscriber
- Microsoft.Extensions.Logging providers are replaced with redacted stderr/file logging; protocol trace/debug bodies are not formatted
- Stdout is reserved for SDK JSON-RPC traffic; application/external-process output uses existing MessageLogger/captured-output paths
- Mutations hold Task 1's machine lease; saved queries reload from FileService and avoid ProfileService.LoadProfile side effects
- WorkflowContext is created inside the runner action and lazily constructs services, so subsequent operations use current settings rather than stale constructor-cached paths
- Settings writes reload installed JSON, environment-specific JSON and environment overrides into the same AppConfig instance used by the runner/redactor; credential properties are excluded from settings responses
- Package publishing always uses the call's required boolean and restores the previous in-memory setting afterward; CLI's local-only build configuration remains in place
- Operation results include IDs, caller request IDs, status, structured data, diagnostics and partial-change indication; failed/cancelled/busy outcomes set MCP IsError
- Operations await their actual worker task; synchronous services are not abandoned on cancellation. Cancellation checks occur at supported operation/loop boundaries
- SDK-requested progress is adapted from already-redacted runner messages
- Status bypasses the execution gate. Listing returns the newest 100 lightweight summaries; operationId returns full retained details. Request IDs correlate retries but do not deduplicate work. History is memory-only and disappears on restart
- Headless Git disables terminal input, Git/GCM interactive authentication and askpass, and uses SSH batch mode with strict host-key checking. The mode is enabled only by McpHost and restored on host exit. Killed Git work is awaited and its output drained before completion/cancellation is reported

## Ownership and package correctness

`DeploymentOwnership.Verify` extracts the CLI helper into shared code. It checks model names, link type/target, source path and a single managed ledger record belonging to the selected profile. Program delegates to this helper. Full affected sets are validated before the first destructive action. Missing-link undeployment also repairs saved flags and removes stale owned ledger entries.

NuGet synchronization affects every package model in the selected repository. Operations validate and undeploy that entire set, reconcile references/membership through the real package service, verify configured packages have restored payloads, save fresh state, and redeploy surviving previously deployed members. Legacy standalone NuGet membership removal remains coherent; feed restoration still requires a repository, as in the existing application. Existing standalone payloads remain usable for deployment.

Preparation persists changed local state only and does not export linked artifacts. No-op reconciliation does not save the profile. Explicit package edits may export changed state, following the existing artifact contract. All WorkflowContext package services disable independent legacy link cleanup; coordinated ownership-aware operations handle link removal using the original saved membership.

Profile switching composes verified shared operations instead of invoking the service's force-replacement switch deployment path. Repository-import orchestration accepts an optional typed import callback so the MCP operation can validate the imported definition and existing deployment set before overwrite.

Small service integration changes:

- DeployablePackageService reports exact generated nupkg/nuspec paths via `LastBuiltArtifactPaths`, exposes configured package versions and the existing package URL parser, and removes stale members before re-preparing after removal
- Empty package references reconcile to an empty package-model set rather than failing package-context setup
- ModelDeploymentService accepts optional `preparePackage` on single-model deployment so already-coordinated MCP package preparation is not repeated inside deployment
- ProfileService repository import accepts an optional import callback; package overview URL updater is shared internally
- GitHelper's main comparison has an optional no-fetch mode, and MCP-specific prompt/cancellation handling
- Shared project references Microsoft.Extensions.Configuration.EnvironmentVariables for effective settings reload

No persisted profile/model/repository contract was changed.

## Verification

Initial test-first run failed with missing MCP namespace, WorkflowContext and SDK references before implementation. Subsequent regression tests reproduced missing-link inspection failure and final-package removal failure before their fixes. The first protocol-progress test used WithProgress with CallAsync; SDK source inspection showed that direct CallAsync needs its progress argument explicitly, and the test was corrected.

McpToolTests covers:

1. Real isolated profile CRUD, fresh external saved-state changes and explicit name/target validation
2. Saved queries without deployment/source directory creation
3. Package removal-set expansion across repository members
4. Structured error adaptation and configured-secret redaction
5. Missing-link live inspection and refusal to delete physical directories
6. Real symlink plus matching managed ownership ledger verification
7. Settings reload changing subsequent service paths and runner redaction
8. Request-ID status lookup while synchronous tool work is still running
9. Real child-process official-SDK discovery of all 52 tools, hidden injected schema parameters, profile creation, progress, redacted structured errors and status lookup
10. Real package-reference and all-member removal preserving source and standalone compiled models
11. Whole-package ownership preflight leaving the first owned link/reference untouched when another member is unsafe
12. Linked reimport preserving local name and default AXDB override
13. Missing-link undeployment repairing saved flag and owned ledger without service control

Final verification:

```powershell
dotnet build FODevManager/FODevManager.csproj --verbosity quiet
dotnet test FODevManager.Tests/FODevManager.Tests.csproj --filter "FullyQualifiedName~McpToolTests|FullyQualifiedName~OperationRunnerTests|FullyQualifiedName~CliHostTests|FullyQualifiedName~ProfileChangeDetectorTests" --logger "console;verbosity=normal"
git diff --check
```

- Build succeeded: **0 errors**, 19 existing nullable/platform warnings
- Focused tests: **75 passed, 0 failed, 0 skipped**, reported total time 8.546 seconds
- Breakdown: 13 McpToolTests, 22 OperationRunnerTests, 35 CliHostTests and 5 ProfileChangeDetectorTests
- All 52 tool registrations were verified through the real child-process SDK discovery response, including exclusion of injected progress/cancellation parameters from schemas
- All 26 touched text files were normalized to CRLF with BOM preservation and checked for stray CR/LF
- `git diff --check` passed; branch remained `feature/mcp-stdio`, with no staging or commits

## Integration concerns and follow-up boundaries

- Task 3 still needs CLI/WinUI mutation-boundary coordination using the same machine lock. Current MCP locking alone does not coordinate desktop writers that have not yet adopted it
- Task 4 remains responsible for comprehensive protocol cancellation/disconnect/shutdown tests, published-executable handshake, combined packaging, end-user documentation and full baseline comparison
- Real FO compilation, W3SVC/database changes, authenticated feeds, remote Git changes, Visual Studio launch and publishing were not executed during verification. The tools invoke actual existing workflows; their external prerequisites still apply
- Repository package URL parsing retains the service's supported Azure Artifacts overview URL format. General Git repository URL import retains the application's existing repository-folder/profile-definition discovery behavior
- No remote publishing, release-tag execution or agent Git commit was performed
- Existing five unrelated baseline failures were not fixed
- No appsettings.json source file was edited; the user's pre-existing WinUI configuration change remains present
- Failures can leave partial filesystem, repository, package-reference or deployment changes. The result explicitly reports this possibility; no rollback is claimed
- Machine-lock ACL/cross-elevation provisioning remains the Task 1 integration concern

No subagents were spawned.

## Task 2 reviewer correctness fixes

All four requested findings have been addressed. Tool inventory remains 52; persisted contracts and SDK integration are unchanged.

### 1. Existing source destinations

`ModelOperations.Create` now checks the real `DefaultSourceDirectory/modelName` destination before constructing creation services. Existing directories, files, links and linked ancestors are rejected, including destinations used by other profiles. Existing models remain available through `models_add_path`. The creation tool remains non-destructive with a description explicitly documenting the new-destination requirement.

### 2. Installed conversion and collection preflight

Added focused `ModelPathOperations` and `ModelPathSafety` helpers. Add-from-path normalizes paths and uses directory boundaries rather than a string-prefix test. Installed conversion requires the exact deployment child matching modelName. A verified link already registered to the selected profile is a non-destructive no-op; foreign/unmanaged links are refused. Physical conversion requires unregistered source metadata with a matching descriptor, a new disjoint destination, and no linked ancestors/descendants.

The conversion service repeats destination/source preflight before copying, verifies a SHA-256 manifest of the copied files (including extensionless files) before saving membership, and verifies it again before physical deletion. Service restart is in a finally block around deletion. Failed validation retains the physical source. Normal discovery materializes and validates all candidate names, roots, project files and metadata before adding the first model or modifying its solution.

The shared installed-path classifier also uses normalized directory boundaries. The add tool remains marked destructive because legitimate physical conversion deletes the original after verified copying.

### 3. Refresh/import package ownership

Refresh no longer calls `CheckProfile` or `LoadProfile`, whose orchestration can reconcile package versions and membership. It observes live ownership and Git state against the existing saved membership and saves locally. Both updated and removed package references leave deployed model source paths/membership intact until explicit coordinated preparation is requested.

`DeployablePackageService` now accepts `allowLegacyDeploymentCleanup` (default true for existing callers). WorkflowContext always supplies false, including the package service used by repository/profile imports. This prevents imported legacy versioned package members from causing independent deletion of foreign/unmanaged reparse points. Existing-profile import continues to preflight/undeploy the original saved collection before replacement; service reconciliation cannot delete additional links behind that operation.

### 4. No-op preparation and incoming linked definitions

Synchronization compares serialized local profile state before/after reconciliation and only saves actual changes. `Prepare` always requests local-only persistence, including when it adds/restores package models. A source-only no-op leaves the local profile write time unchanged. Both no-op and changed preparation leave a pending linked definition byte-for-byte intact. Profile switching uses this same preparation path after checkout.

### Regression evidence

Regression tests were added before implementation. The initial run reproduced eight failing cases covering installed conversion, sibling-prefix handling, whole-collection preflight, and both preparation-export scenarios. The creation fixture was strengthened to check the entire destination structure because missing test-host templates caused the old implementation to fail only after creating directories. The cached-package fixture was corrected to include the local origin marker required by the application's repository detector. Before implementation, a focused rerun then reproduced the remaining three failures: destination mutation, foreign legacy link deletion during import, and refresh moving the saved package source/version while retaining the old deployment.

Added 15 regression/verification cases:

- Cross-profile source destination refusal with original project/descriptor and directory structure intact
- Installed path/name mismatch, existing output, foreign symlink and unmanaged symlink refusal (four cases)
- Sibling-of-deployment-root compiled model registration without conversion
- Whole-collection validation before adding a valid first compiled member followed by an invalid source member
- Refresh with changed version references and removed references, preserving an actual owned v1 symlink and its original membership (two cases)
- Import with a foreign legacy versioned symlink retained alongside its ledger entry
- Source-only no-op and changed cached-package preparation retaining incoming linked JSON; no-op also retains local profile timestamp (two cases)
- Successful existing-source registration preserving source project/descriptor bytes
- Existing own managed installed symlink treated as a no-op
- Physical conversion preflight rejects nested symlinks and copy verification detects changed bytes

The tests use temporary profiles, cached fake package payloads, local empty Git repositories, and real temporary symlinks. No network feed, real FO metadata, database or W3SVC mutation is performed. The old conversion reproduction temporarily suppresses service control through the existing InOperation state and restores that state in finally.

Verification after review fixes:

```powershell
dotnet test FODevManager.Tests/FODevManager.Tests.csproj --filter "FullyQualifiedName~McpToolTests|FullyQualifiedName~OperationRunnerTests|FullyQualifiedName~CliHostTests|FullyQualifiedName~ProfileChangeDetectorTests" --logger "console;verbosity=normal"
```

**90 passed, 0 failed, 0 skipped**, reported total time **7.5315 seconds**: 28 McpToolTests, 22 OperationRunnerTests, 35 CliHostTests and 5 ProfileChangeDetectorTests. This supersedes the initial Task 2 counts above. Existing unrelated baseline failures remain untouched.

`dotnet build FODevManager/FODevManager.csproj --verbosity quiet` succeeded with 0 errors and 19 existing warnings. All 13 review-touched text files were normalized/verified as CRLF with BOM preservation; `git diff --check` passed. No appsettings edits, commits or subagents were used for these fixes.
