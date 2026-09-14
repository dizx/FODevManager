# MCP server

FO Dev Manager exposes **52 tools** through the official C# ModelContextProtocol SDK (2.2.0). Run the installed `fodev.exe mcp` as a stdio child process. **WinUI does not need to be running.** There is no listening network port or UI automation.

## Client configuration

Use your MCP client's stdio server configuration, for example:

```json
{
  "mcpServers": {
    "fodev": {
      "command": "C:\\Program Files\\FODevManager\\fodev.exe",
      "args": ["mcp"]
    }
  }
}
```

Replace the command with the actual installed path. Use a Windows account with access to the desired profiles, repositories and environment. Clients can supply an `env` dictionary for overrides such as `ProfileStoragePath`. Stdout contains newline-delimited MCP JSON-RPC only; diagnostics go to stderr and `%APPDATA%\FODevManager\Logs\fodev-mcp-*.log`. Do not wrap the executable in a script that prints banners to stdout. The client initializes the session, sends `notifications/initialized`, discovers schemas with `tools/list`, then invokes `tools/call`.

Example call parameters:

```json
{"name":"profile_show","arguments":{"profileName":"DevProfile","requestId":"inspect-001"}}
```

Tool schemas are the authority for required arguments/types. Profile/model names, repository IDs, paths, branch/stash choices and publishing choices replace desktop selection, pickers and dialogs. Obtain repository IDs from `repositories_list`; do not assume every model belongs to a repository.

## Complete desktop action mapping

This checked 52-name inventory is compared with both `tools/list` and the reviewed Task 2 inventory. Window chrome, selection, expansion and badges map to data queries rather than presentation tools.

| Tool | Desktop action and actual behavior |
| --- | --- |
| `profiles_list` | Profile dropdown: fresh saved names |
| `profile_show` | Selected profile information: saved model, deployment and Git flags |
| `profile_active` | Active environment indicator: saved active profile names |
| `profile_create` | Create profile and solution; mark it active |
| `profile_delete` | Delete profile after whole-set owned-link validation and undeployment with service control |
| `profile_import_file` | Import explicit file and optional local name; clone/prepare repositories, models and solution; may overwrite target profile |
| `profile_import_repository` | Repository URL import; clone and find/import profile definition, prepare models and solution |
| `profile_export` | Portable export to explicit new file; return artifact path; refuse overwrite |
| `profile_refresh` | Refresh live ownership/Git state and save locally; preserve membership, package paths and linked definitions; nuget_prepare performs reconciliation |
| `profile_switch` | Ownership preflight, undeploy, preferred branches/stashes, package restore/deploy, database application and service control |
| `profile_sync_check` | Linked-definition revision check; may bootstrap missing export or migrate legacy export |
| `profile_sync_dismiss` | Dismiss explicit revision only if still current; persist locally |
| `profile_sync_reimport` | Undeploy owned links and reimport linked definition, preserving local name/database including AXDB |
| `models_list` | Saved source, compiled and NuGet members across repositories and standalone models |
| `model_show` | Saved model properties, paths and type |
| `models_add_path` | Collection preflight and source/compiled discovery; physical installed conversion requires exact name/path and new destination, verifies copied bytes before deleting original; refuse foreign/unmanaged links |
| `model_create` | Create source model/project in new destination and include in solution; reject existing destinations |
| `model_remove` | Remove membership/solution project after owned undeployment; NuGet removal expands to package members and repository reconciliation |
| `model_properties_update` | Set source model Main FO solution-root role; selecting clears role on other models |
| `model_version` | Live source/compiled version or saved NuGet version |
| `model_version_update` | Source version editor: non-negative major/minor/revision inputs passed to existing descriptor version service |
| `repositories_list` | Repository grouping: saved data |
| `repository_show` | Repository properties: saved data |
| `repository_properties_update` | Edit display name, preferred branch, checkout/stash preferences, task and comment; omitted fields stay saved |
| `repository_status` | Live local branch, dirty state, upstream health, HEAD and local main comparison; no implicit fetch |
| `git_fetch` | Fetch/prune remotes for repository or whole profile; authentication must be noninteractive |
| `repository_remote` | Remote URL data and optional explicit browser open |
| `git_assign_task` | Assign task with explicit branch switching, branch name, stash-dirty and branch-creation choices |
| `repository_task_url` | Task URL data and optional explicit browser open |
| `git_merge_main` | Merge configured main into current branch; may leave conflicts; use git_fetch/repository_status for explicit current comparison |
| `git_reset_profile` | Stash dirty repositories, checkout configured main, fetch and pull throughout profile |
| `git_tag_release` | Bump changed source revisions, commit descriptors, create release tags and **push tags remotely**; requires clean main/release branches |
| `nuget_versions` | Version dropdown: selected repository's configured package feed |
| `nuget_add` | Add package URL to required repository's Build/isv.config, restore/reconcile membership with verified deployment handling |
| `nuget_update` | Change repository package version/URLs; reconcile membership and deployments |
| `nuget_remove` | Remove reference and all package members after full affected repository synchronization/deployment preflight |
| `nuget_prepare` | Resolve dependencies on detached graph before replacing changed deployments; no-op preserves links/profile timestamp; local saves preserve incoming linked definitions |
| `package_build` | Source compilation or compiled payload packaging; required publish boolean: true pushes to feed, false builds locally; return actual package/nuspec paths |
| `deployment_inspect` | Live targets, ledger and ownership; no saved-state repair/self-heal |
| `deployment_deploy` | Deploy model or profile using source/compiled symlinks; prepare packages, control W3SVC and verify ownership |
| `deployment_undeploy` | Preflight model/profile set, then remove owned links with service control |
| `deployment_undeploy_all` | Preflight managed deployments across saved profiles, then undeploy links under actual owners with service control |
| `solution_path` | Resolve saved solution location |
| `solution_ensure` | Create/update solution and include all source projects; return solution path |
| `solution_open` | Open resolved existing solution in Visual Studio |
| `database_get` | Saved profile database display |
| `database_set` | Save database name; active profiles also apply to web configuration with service control |
| `database_apply` | Apply saved database to local FO web configuration with service control |
| `application_about` | Version/about and execution/history semantics |
| `settings_read` | Effective non-secret settings; credential-presence indicator only |
| `settings_update` | Update supported typed settings in installed JSON and reload effective configuration; credentials input-only |
| `operation_status` | Running/queued/completed lookup outside execution gate; operation ID or request ID filters |

Read-only/destructive SDK annotations describe behavior; they do not grant permissions or make work transactional. Non-destructive tools can still write files, fetch Git refs or open applications. Read tool descriptions before invocation.

## Saved versus live state

Profile/model/repository lists/details load saved profiles afresh per operation without desktop service-loading side effects. Saved active/deployment/Git flags can be stale. Use `deployment_inspect` and `repository_status` for live observations; status does not fetch. `profile_refresh` saves local observations without reconciling package membership; explicit `nuget_prepare` does dependency reconciliation.

Repository-backed and standalone source/compiled models are supported. Legacy standalone NuGet payloads can be inspected, deployed and removed coherently; feed restore/add/update requires repository package configuration. Removing a NuGet model can affect its entire package.

## Results, history and cancellation

Most calls await completion and return an envelope in `structuredContent`, also serialized in text content: `operationId`, optional `requestId`, `name`, `status`, `data`, `diagnostics`, `createdAt`, `startedAt`, `completedAt`, `partialChangesPossible` and `droppedDiagnosticCount`. Nested `data` preserves service property casing (for example settings `CredentialsConfigured` and update `Effective`); do not assume all nested keys are camelCase. Status is queued, running, succeeded, failed, cancelled or busy. Failed/cancelled/busy operations set MCP `isError=true`. Protocol errors such as unknown JSON-RPC methods use JSON-RPC errors. Check the structured outcome, not just transport completion.

Caller `requestId` is **correlation, not deduplication**: repeating it executes another operation and status may return multiple matches. Use short, unique, non-secret IDs; long IDs are bounded and secrets redacted. `operation_status` with `operationId` returns full retained details; request-ID/no-filter queries return newest 100 lightweight summaries and total matches. A filtered no-match is an error result.

History retains the latest **100 completed operations**, plus queued/running work; diagnostics are bounded to 100 entries per operation. History is **memory-only in one server process** and disappears on restart. A new stdio child has new history even when the client calls it a reconnect. Request IDs cannot recover history from a replaced/killed process.

All ordinary MCP operations serialize within the server, including reads. `operation_status` bypasses that gate while work runs/waits. Include `_meta.progressToken` on `tools/call` to receive `notifications/progress`: redacted application messages with increasing counters, not percentage estimates.

Send `notifications/cancelled` with the **JSON-RPC request ID**, distinct from the tool's optional application `requestId`. Cancellation is cooperative: queued work can cancel before execution; active work checks only at supported boundaries. Synchronous noncooperative work retains running status and the mutation lease until it actually returns. If it succeeds despite cancellation, retained status is succeeded, not falsely cancelled. A client may stop awaiting the original response; query status in the same live session before retrying. Headless Git cancellation kills and drains its child before reporting completion.

The SDK suppresses the original response after a cancellation notification; retained operation history is the completion/recovery channel. Stdio shutdown is EOF (close stdin), not a custom shutdown tool. Active work can delay normal exit, and EOF is not a cancellation acknowledgement. Forced termination can interrupt writes and leave external effects. Failure/cancellation after mutation begins reports possible partial changes; no rollback is promised. Database set may persist before apply fails; branches, stashes, artifacts, package references and links may already have changed. Service-controlled workflows can start W3SVC even if initially stopped.

## Cross-host coordination

MCP, CLI and WinUI mutation boundaries use `%ProgramData%\FODevManager\application-operation.lock`, an exclusive file lease held through actual work and cleanup. Another host's mutation returns busy without executing. MCP waits for its own gate, then attempts the machine lease non-blockingly. Nested services compose inside the outer lease without reacquiring.

All participating accounts/elevation contexts require access to the same lock directory/file and data paths. Provision appropriate Windows ACLs at installation; access errors fail closed rather than using another lock. Older versions, external editors and tools bypassing these boundaries are outside coordination. WinUI observes saved changes through refresh/load rather than live IPC; mutation boundaries reload before writing and property dialogs patch edited fields only.

## Configuration and credentials

Precedence: installed `appsettings.json`, then `appsettings.{EnvironmentName}.json`, then environment variables (highest). Files resolve beside the executable, independent of client working directory. Environment defaults to Production; `DOTNET_ENVIRONMENT` selects it. Combined installation shares canonical WinUI settings with the console; separate console output has its own settings.

`settings_read` returns **effective in-memory** values. `settings_update` writes supplied non-null supported fields to installed `appsettings.json`, then reloads all layers into that process for subsequent operations. No restart is needed for that update; new services use current paths. Overrides can mask successfully saved local values. MCP does not watch external file edits: restart or perform a supported settings update to reload. CLI reloads on launch; WinUI reloads at coordinated mutation boundaries. Changes to a parent's environment require a new child process.

Supported updates: source/profile/deployment/package paths, task URL, model ID bounds, uncommitted-switch check, NuGet executable, publish-on-build preference/feed source, Azure Artifacts username/PAT/API key. Null/omitted means unchanged; empty credential strings clear saved values. The process needs write access to installed JSON. Settings responses omit credentials; configured values and URL credentials are redacted from data, diagnostics, progress and server logs. Prefer configured credentials/environment over credentials embedded in prompts or URLs.

`package_build` requires explicit `publish` on every call. It overrides the automatic publishing preference for that operation and restores the previous in-memory value afterward. `false` is local-only; `true` performs real feed publishing. Ordinary CLI `package-build` remains local-only regardless of settings. `git_tag_release` independently performs real commits and remote tag pushes, unaffected by the package publishing flag.

## External prerequisites and limits

The server starts without FO or WinUI running and saved queries can use isolated stores. Actual workflows still require:

- Windows x64; combined self-contained installation supplies the runtime (source build requires .NET 10 SDK)
- Accessible FO directories, valid source/compiled payloads and configured deployment roots
- Developer Mode or symlink privilege, appropriate W3SVC/service-control and FO web.config permissions for deployment/database/switch
- Visual Studio/FO tooling, MSBuild/compiler packages and NuGet for source builds; unused drive letter for existing short-path build-cache mapping
- Git on PATH and preconfigured noninteractive HTTPS/SSH authentication; MCP disables prompts, askpass and interactive GCM and enforces SSH batch/host-key checking
- nuget.exe on PATH or configured, repository Build/isv.config and nuget.config, feed/network access and credentials for restore/version lookup/publish
- Existing supported Azure Artifacts package overview URL format for package addition; Git import retains existing folder/profile-definition discovery behavior
- Visual Studio/browser and an interactive Windows session for explicit open actions

Repackaging installed CompiledNuget models is unsupported; use the original package. Source builds require fresh output rather than deployed-binary fallback. Source version updates use existing version-service field conventions, without descriptor format migration.

## Build and verification

Combined WinUI publish includes console MCP SDK dependencies and compatible shared dependencies. Keep `PublishTrimmed=false`, `PublishSingleFile=false`, `UseAppHost=true`. Companion publishing sanitizes published credentials, removes NuGet credential sections and excludes Development JSON without modifying source settings.

```powershell
dotnet publish FODevManager.WinUI/FODevManager.WinUI.csproj -c Release -r win-x64 --self-contained true -p:Platform=x64 -o Output/McpVerification
dotnet test FODevManager.Tests/FODevManager.Tests.csproj --filter "FullyQualifiedName~McpProtocolTests"
```

Wire tests parse every stdout line from real children and check inventory, error recovery, private settings, history/correlation, progress and lifecycle. A separate test-only host adds controlled noncooperative work to the production host/runner; fixture tools are never registered by the installed executable. Set `FODEV_MCP_TEST_EXECUTABLE` to an absolute published fodev.exe path for production wire tests against that installation. Controlled/settings-write tests continue using isolated copied hosts. See the Task 4 report for baseline and publish evidence.
