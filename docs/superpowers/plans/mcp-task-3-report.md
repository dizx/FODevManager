# Task 3 implementation report

## Scope and result

Implemented Task 3 on `feature/mcp-stdio` in `C:\Dev\FODevManager` after reading the plan, global constraints, design spec, and Task 1/2 reports. No subagents, staging, commits, remote publishing, or source appsettings.json edits were performed. Existing Task 1/2 work and the user's WinUI configuration edit are retained.

CLI and WinUI mutations now use the same default `ApplicationOperationLock` as MCP: `CommonApplicationData/FODevManager/application-operation.lock`. Task 1's lock and runner implementations were not changed.

Task 3 files:

- `FODevManager/Program.cs`
- `FODevManager.WinUI/App.xaml.cs`
- `FODevManager.WinUI/Framework/BusyOps.cs`
- `FODevManager.WinUI/MainWindow.xaml.cs`
- `FODevManager.WinUI/MainWindow.ModelSelection.cs`
- `FODevManager.WinUI/ViewModel/SettingsViewModel.cs`
- `FoDevManager.Shared/Services/FileService.cs`
- `FoDevManager.Shared/Operations/HostOperationBoundary.cs`
- `FoDevManager.Shared/Operations/PropertyEdits.cs`
- `FoDevManager.Shared/Operations/ModelVersionEdits.cs`
- `FODevManager.Tests/ApplicationOperationLockTests.cs`
- This report

## Boundary and lifetime contract

`HostOperationBoundary.Acquire`, `Run`, and `RunAsync` use the authoritative `TryAcquire()` disposable lease API. Contention logs a MessageLogger error naming the requested operation and asking the user to retry, then fails without invoking the action. Filesystem/access errors also fail closed. Async actions retain ownership through their actual completion and async finally blocks, even when cancellation has been requested and the underlying service cannot stop yet.

There is deliberately no AsyncLocal ownership bypass. Independent async requests cannot inherit a lease or permission to mutate. Compose nested services inside the outer action without calling another host boundary. An erroneous nested acquisition fails non-blockingly rather than deadlocking. MCP continues composing services inside its existing runner boundary.

BusyOps remains the desktop foreground boundary, including its existing RunOperationAsync callers, TryResult values, UI dispatch, busy overlay, and W3SVC lifecycle. The shared lease encloses configuration reload, service construction, work, W3SVC cleanup, and busy-state cleanup. Busy contention never enters the busy/W3SVC body. Busy-state cleanup now also runs when service restart throws. Cancellation and exceptions unwind before the shared lease is disposed.

## Exact CLI inventory

All commands enter through `Program.Execute`. Parsing, help, and usage validation precede lock acquisition. Mutators acquire once before `buildHost`, service resolution, fresh profile loads, service-control checks, and execution. Host disposal is also within that lease.

| Commands | Coordination |
| --- | --- |
| `help`, invalid usage | Return before host construction and acquisition |
| `list`, `show`, `repos`, `solution-path` | Existing saved-state query path remains available during a mutation; read-only FileService construction does not create a missing profile-store directory |
| `create`, `import`, `delete`, `check`, `switch` | One lease around complete profile workflow |
| `add`, `remove`, `deploy`, `undeploy` | One lease including target reload, ownership validation where already supported, service control, and all nested model actions |
| `db-set`, `db-apply` | One lease including active-state reload and environment changes |
| `git-fetch`, `git-status`, `git-open`, `peri` | One lease; conservative coverage includes constructor side effects of legacy service-based commands |
| `export`, `solution-ensure`, `open-vs`, `package-build` | One lease including filesystem generation and service construction |

The CLI retains both existing local-build-only protections: BuildHost initializes `PushDeployablePackageOnBuild = false`, and package-build sets it false again before invoking the build service. MCP settings changes cannot enable CLI publishing.

## Exact WinUI foreground inventory

Paths below are in `MainWindow.xaml.cs` unless a different file is named. Every listed mutation enters BusyOps exactly once; service methods called from its action do not reacquire.

| UI path | Work inside the boundary |
| --- | --- |
| `CreateProfile_Click` -> `CreateProfile` | Create profile, solution, active-state persistence |
| `ImportFromFile_Click` -> `ImportProfile` | Import after file picker completes |
| `ImportFromRepo_Click` | Clone/import after repository URL dialog completes |
| `DeleteProfile_Click` | Direct DeleteProfile call moved into RunOperationAsync after confirmation |
| `RefreshProfile_Click` -> `CheckProfile` | Mutating service check/reconciliation explicitly coordinated |
| `SwitchProfile_Click` -> `SwitchProfile` | Complete switch, branches, package preparation, deployment, database and activation after confirmation |
| `CreateModel_Click` -> `CreateModel` | Model/project creation after naming dialog |
| `AddModel_Click` -> `AddModelToProfile` | Add/discovery/conversion workflow |
| `AddModel_Click` -> `AddNugetModelToProfile` | NuGet addition after repository picker; only selected repository ID crosses the dialog boundary |
| `RemoveModel_Click` -> `RemoveModelFromProfile` | Removal after confirmation, including nested package/model work |
| `DeployModel_Click` -> `DeployModel` | Single-model deployment and preparation |
| `UnDeployModel_Click` -> `UnDeployModel` | Single-model undeployment |
| `DeployProfile_Click` -> `DeployAllModels` | Complete profile deployment |
| `UnDeployProfile_Click` -> `UnDeployAllModels` | Complete all-profile undeployment |
| `MainWindow.ModelSelection.cs: RunSelectedModelDeploymentAsync` | One lease for the entire selected-model batch; reload and preflight all targets before work, reject missing/changed type/source paths, and aggregate execution failures |
| Context menu -> `BuildDeployablePackageForModel` | Source/compiled package build, retaining the desktop's configured publishing choice |
| `AssignTask_ForRepo_Click` | Assign task/branch work after task dialog, targeting saved profile name and repository ID |
| `RepoHeader_GitMerge_Click` -> `EnsureMergedWithMainAsync` | Force fetch and strict comparison under a short shared boundary, release before the prompt, then reload repository by ID and merge under BusyOps after confirmation |
| `GitActions_ResetToMain_Click` | Reload profile inside action after confirmation, then reset all repositories |
| `GitActions_TagRelease_Click` | Reload profile inside action after confirmation, then release/version/tag workflow |
| Database Apply button / Enter -> `ApplyDatabaseNameChangeAsync` | Capture target profile name before dialog; save database against fresh service load; refresh saved state after success |
| `ShowRepositoryPropertiesAsync` | Capture edited fields, acquire after Save, reload repository, patch only changed editable fields, persist |
| `ShowModelPropertiesAsync` | Capture edits, acquire after Save, reload model, validate type, patch changed model fields and changed source-version components, persist/update package |

Foreground handlers with explicit success messages now stop on a busy/failed boundary rather than announcing success after rejected work. The unreachable duplicate database-save handler body was removed; both Apply and Enter use the same coordinated path.

## Exact WinUI background, settings and read inventory

| Path | Coordination and stale-state handling |
| --- | --- |
| `App` constructor -> `AppSettingsMigration.RunOnStartup` | Separate shared boundary around runtime settings migration; busy/failure is logged and migration deferred |
| `App.OnLaunched` | No eager mutation-service resolution; MainWindow receives AppConfig only, avoiding deployment/source/package directory creation during startup outside a workflow |
| `QueueNugetPreparation` | `RunBackgroundMutationAsync` -> shared boundary -> effective settings reload -> `PackageOperations.Prepare`; resolve and validate the complete repository set on a detached graph before changing deployments; no-op leaves links/services/profile untouched; changed preparations persist locally and refresh the view |
| `MonitorLoopAsync` -> `RunGitHealthCheckAndUpdates` | Shared background boundary covers fresh profile/repository loads and the entire Git-health loop, including fetch |
| `RefreshRepositoryHealthAsync` | Called only within the preceding background boundary; updates view-model branch/health fields without writing a saved profile snapshot |
| `RunModelSyncCheckAsync` check phase | Shared background boundary loads current saved profile before CheckProfileModelChangesAsync, including bootstrap export and any service saves |
| Sync prompt | Shared lease is released before showing/awaiting the dialog |
| Sync dismissal | Reacquire, reload, and call Task 2 DismissSync to verify the exact revision before persistence |
| Sync reimport | Reacquire through BusyOps after dialog, use Reimport with explicit outer-owned service control, fresh saved path/name/database and cancellation token; no old monitoring snapshot is saved |
| Model dialog package-version lookup | Independent background boundary covers package-service construction/cache-directory side effects and feed lookup; it ends on lookup completion and does not wait for the dialog |
| `SettingsPage.OnSaveClick` -> `SettingsViewModel.Save` | One shared lease for all changed settings writes and effective reload; dialog baseline is retained on failure, and only user-edited settings are written |
| Settings folder/executable pickers and password-change callbacks | Edit view-model values only; actual persistence is confined to Save |
| `LoadProfilesAsync`, `GetAllProfiles`, `LoadProfileByName`, `BuildProfileLoadResult`, selection and view refresh | Saved-state reads through FileService with `ensureDirectory: false`; no UpdateDeploymentStatus or ProfileService.LoadProfile calls, no write or lock reacquisition during rendering |
| `OpenGitForRepo_Click` -> `OpenGitRepo` | Fresh saved lookup plus live Git remote lookup/browser open without constructing mutation services |
| `OpenSolution_Click`, task/package/about URLs, version display, database display | Read/presentation/external-open actions, no profile/settings persistence |
| Model/repository view models and grouping/selection | Audited constructors and setters: in-memory presentation only |
| UI heartbeat, logging subscribers, crash logging | Diagnostics only; no profile/repository/settings/deployment mutations |

MainWindow's mutation-service properties construct fresh WorkflowContext services when invoked inside the boundary. This also avoids retaining constructor-cached configuration paths or ProfilesContainer graphs across independent operations. FileService gained an optional `ensureDirectory` parameter, defaulting to the existing behavior for other callers; WinUI rendering explicitly disables directory creation.

`PropertyEdits<T>` captures an explicit allowlist of changed values before worker dispatch, then applies those values to freshly loaded state. Repository name/branch/stash/task fields that were not edited do not overwrite external updates. Model membership, paths, deployment flags and unedited package versions remain fresh. `ModelVersionEdits` merges major/minor/revision separately, so editing major does not overwrite an external minor/revision change. Settings Save likewise preserves untouched external settings and credentials while retaining the desktop's existing editable choices.

Effective configuration is reloaded under the mutation boundary using the existing Task 2 SettingsOperations API. WinUI startup now uses the same environment-variable precedence. Newly constructed mutation services observe those effective values. MCP's existing subsequent-operation settings reload/service reconstruction and credential-redaction tests remain passing.

## Verification

Commands:

```powershell
dotnet test FODevManager.Tests/FODevManager.Tests.csproj --filter "FullyQualifiedName~ApplicationOperationLockTests|FullyQualifiedName~CliHostTests|FullyQualifiedName~McpToolTests|FullyQualifiedName~OperationRunnerTests|FullyQualifiedName~ProfileChangeDetectorTests" --logger "console;verbosity=normal"
dotnet build FODevManager.WinUI/FODevManager.WinUI.csproj -p:Platform=x64 --verbosity quiet
git diff --check
```

- **100 passed, 0 failed, 0 skipped**, reported total time 9.8131 seconds
- Breakdown: **10 new lock/host/stale-state tests**, 35 CliHostTests, 28 McpToolTests, 22 OperationRunnerTests, 5 ProfileChangeDetectorTests
- WinUI build succeeded with **0 errors, 33 warnings** from existing nullable/platform warning sites, including repeated WinUI compilation diagnostics
- The targeted test command also compiled the console host and shared project
- Final CLI read-only construction refinement was followed by `dotnet test FODevManager.Tests/FODevManager.Tests.csproj --filter "FullyQualifiedName~ApplicationOperationLockTests|FullyQualifiedName~CliHostTests" --verbosity quiet`: **45 passed, 0 failed, 0 skipped**, including the strengthened real-child assertion that a read command does not create a profile-store directory while the machine lease is held
- The first test build caught an incorrect test reference to Message.Text; it was corrected to the actual Message.Content API before the successful runs
- No full-suite run was performed; the documented five unrelated baseline failures were not changed

New tests cover a real PowerShell child attempting the exact exclusive-file lease while nested async work is pending and after completion; real child CLI contention against the production machine lock while `list` still works; CLI busy rejection before host construction; independent async requests and invalid nested boundary calls; cancellation/failure disposal; lease retention through async failure cleanup; fresh repository field patches; fresh model and version-component patches; edited-only settings with external changes; and read-only FileService construction during another operation.

The initial 12 Task 3 text files were normalized to CRLF with existing BOMs preserved and checked for stray CR/LF. Review changes and their verification are recorded below. No unrelated files were normalized. Whitespace and final branch/workspace status were checked without staging or committing.

## Concerns and integration limits

- Machine-lock directory ACL/cross-account/elevation provisioning remains the Task 1 installation concern; access failures do not fall back to an uncoordinated lock
- WinUI was compiled and its host paths audited; live desktop clicking, real FO compilation/deployment, W3SVC/database changes, authenticated feeds, release pushes, and Visual Studio launching were not exercised
- Foreground services retain their existing nontransactional behavior and error reporting; coordination does not imply rollback after a partial failure
- Background busy work is rejected, not queued behind another host; later monitoring/refresh can retry, and settings Save leaves the user's edits available to retry
- Rendering intentionally shows saved deployment flags until a coordinated explicit check updates them; view refresh itself no longer reconciles/deploys/saves
- Runtime settings migration is deferred if busy; an installation with no readable settings file still requires valid configuration before WinUI startup can finish
- External editors/older hosts that do not use the shared lock are outside this coordination contract
- Task 4 still owns full baseline comparison, comprehensive protocol cancellation/shutdown verification, published-executable handshake, packaging, and end-user documentation

## Task 3 review corrections

All four review findings were addressed without changing lock ownership, dialog boundaries, property-edit patches, or Task 1's runner. The corrections use the same machine lease and preserve existing BusyOps entry points.

### 1. Reimport inside desktop-owned service control

WorkflowContext now has an explicit `OuterOwnsServiceControl` policy and a `RefreshServiceState` delegate. DeploymentOperations calls `PrepareServiceControl`: headless/default workflows refresh as before; outer-owned workflows require the actual W3cServiceState.InOperation contract to be active and do not refresh it. MainWindow's sync reimport explicitly opts into outer-owned control because BusyOps has already stopped the service and entered that scope. This is not an ambient skip based on a busy flag and does not bypass or reacquire the operation lock.

The regression test creates an actual owned symlink and ledger entry, sets the same InOperation/IsRunning state as BusyOps after stopping W3SVC, verifies that a direct nested RefreshW3SVCState would throw, then runs shared Reimport with outer ownership. Reimport removes the old link/record, preserves the local database, and leaves the outer service scope active. A throwing refresh delegate ensures no nested refresh occurs; the existing ServiceHelper scope suppresses real service start/stop commands.

### 2. Automatic preparation resolves before deployment changes

PackageOperations.Prepare no longer calls the explicit-edit Synchronize path, which undeploys first. Preparation now:

1. Loads and verifies original owned package deployments
2. Clones the profile graph and resolves every repository using the existing package service with legacy link cleanup disabled
3. Validates configured versions, discovered membership, actual compiled payloads and unique model names before any undeployment
4. Returns immediately when the resolved graph matches the saved graph
5. Preflights added names and identifies only removed/replaced package models whose payload/version/identity changed
6. Undeploys only those affected models, saves the resolved profile locally, and redeploys surviving previously deployed members

Unchanged preparation retains the original link, ledger, flags, file contents and profile timestamp, with no service-state refresh. A resolution/credential failure leaves a healthy previous package deployed. A controlled cached-version replacement test verifies one link deletion/creation for the changed model while an unrelated compiled deployment and its ledger record remain intact. Explicit add/update/remove workflows retain their existing semantics.

### 3. Reliable desktop outcomes and selected-model failures

BusyOps now uses the shared `HostOperationExecution.RunAsync` outcome helper inside its existing lease and OperationScope. Execution-scoped MessageBus capture includes awaited worker tasks and cleanup. False/null results, logged errors, exceptions and cancellation are unsuccessful. The completion highlight and Ok=true occur only after action and cleanup both complete successfully. Service-stop errors abort before the action, and restart errors prevent success while busy-state cleanup still runs.

The helper's AsyncLocal is exclusively diagnostic capture, never lock ownership. It closes and restores capture at completion; tests verify that an independent failing request and unrelated event cannot contaminate an active successful request.

SelectedModelOperations preflights the full selected set against the fresh saved profile before executing any target. It then aggregates false results, logged errors and exceptions across the batch. Missing/stale selections fail rather than producing an empty successful operation. MainWindow snapshots selection models before awaiting and uses this helper; deployment false values are propagated and undeployment checks its postcondition.

ProfileService.CreateModel now returns its underlying creation outcome instead of swallowing false. MainWindow uses the typed BusyOps result, and shared ModelOperations requires success. Thus a failed source creation cannot produce the desktop's Created message merely because the void wrapper returned normally. Existing solution-step logged errors are caught by the outcome capture/runner.

### 4. Fresh remote comparison before an explicit merge

RepositoryOperations.CheckMainUpdates loads the selected repository by ID and performs an unthrottled fetch followed by strict HEAD/origin-main comparison. MainWindow runs that phase under a short background mutation boundary. It releases the lease before waiting for the confirmation dialog, then reacquires through BusyOps, reloads the repository, and requires the actual merge result to succeed. The stale HasMainUpdates click guard was removed; cached view state no longer vetoes an explicit check. Fetch/comparison failure, busy contention, cancellation and declined confirmation do not become successful merge completion.

The regression uses a temporary bare Git remote and two local clones. After an initial up-to-date check, a fixture commit advances remote main while the working clone's cached origin/main remains stale. The explicit check fetches the new commit and reports changes. The test also verifies lease availability during the simulated dialog, busy rejection of the second stage, missing-remote failure and cancellation. No network access or repository-under-development commits are involved.

### Review files and verification

Review-touched files:

- `FODevManager.WinUI/Framework/BusyOps.cs`
- `FODevManager.WinUI/MainWindow.xaml.cs`
- `FODevManager.WinUI/MainWindow.ModelSelection.cs`
- `FoDevManager.Shared/Operations/WorkflowContext.cs`
- `FoDevManager.Shared/Operations/DeploymentOperations.cs`
- `FoDevManager.Shared/Operations/PackageOperations.cs`
- `FoDevManager.Shared/Operations/RepositoryOperations.cs`
- `FoDevManager.Shared/Operations/ModelOperations.cs`
- `FoDevManager.Shared/Operations/HostOperationExecution.cs` (new)
- `FoDevManager.Shared/Operations/SelectedModelOperations.cs` (new)
- `FoDevManager.Shared/Services/ProfileService.cs`
- `FoDevManager.Shared/Utils/GitHelper.cs`
- `FODevManager.Tests/HostCoordinationReviewTests.cs` (new)
- This report

The new fixture was written before the helpers/policies and initially failed to compile on missing APIs. After adding the helper APIs, the existing preparation implementation reproduced both undesired service-control failures. A separate local-Git fixture teardown failure was corrected by clearing read-only attributes on disposable fixture files. The completed fixture contains **17 passing cases** covering all four findings.

Final commands:

```powershell
dotnet test FODevManager.Tests/FODevManager.Tests.csproj --filter "FullyQualifiedName~HostCoordinationReviewTests|FullyQualifiedName~ApplicationOperationLockTests|FullyQualifiedName~CliHostTests|FullyQualifiedName~McpToolTests|FullyQualifiedName~OperationRunnerTests|FullyQualifiedName~ProfileChangeDetectorTests" --logger "console;verbosity=normal"
dotnet build FODevManager.WinUI/FODevManager.WinUI.csproj -p:Platform=x64 --verbosity quiet
git diff --check
```

- **117 passed, 0 failed, 0 skipped**, reported total time **9.4422 seconds**: 17 review cases plus the previous 100-case focused set
- The stale/missing-selection test was then strengthened with a valid first target to prove whole-set preflight prevents even that first action; the review-only command was rerun with **17 passed, 0 failed, 0 skipped**
- WinUI build succeeded with **0 errors, 33 existing warning diagnostics**, reported time 7.11 seconds
- Console host and shared project compiled as part of the test command
- All 14 review-touched text files were normalized/verified as CRLF with BOM preservation; whitespace checks passed
- No checkout commits, appsettings.json edits, subagents or real W3SVC/network-feed operations were performed
- The five documented baseline failures remain outside this focused verification and were not changed

Preparation can still leave partial state if deployment itself fails after validated resolution and mutation begins; the correction prevents selection-triggered no-op churn and pre-resolution undeployment, not transactional rollback. WinUI integration is compile-verified and exercised through its actual shared outcome/service-state contracts rather than a live desktop automation session.

### Finding 4 XAML reachability follow-up

Re-review identified that removing the handler's cached-state guard did not make the action reachable: `MainWindow.xaml` still conditionally loaded GridMergeButton using HasMainUpdates. Removed that button's `x:Load` binding. The repository header now always offers Git Merge, wired to the existing fetch/check/confirm/merge handler, regardless of cached update state. Its tooltip explicitly describes fetching/checking before confirmation. The separate `main updated` badge retains its HasMainUpdates visibility binding.

Verified the actual repository DataTemplate and its parent containers: the button has no conditional load, visibility or enabled binding, and the header is outside the model expansion container. A targeted XAML search confirms that HasMainUpdates now controls only the badge, while GridMergeButton remains wired to RepoHeader_GitMerge_Click.

Focused verification: `dotnet build FODevManager.WinUI/FODevManager.WinUI.csproj -p:Platform=x64 --verbosity quiet` succeeded with **0 errors and 33 existing warning diagnostics**, reported time **8.36 seconds**. No test suites were repeated for this XAML-only correction. Only `FODevManager.WinUI/MainWindow.xaml` and this report were edited and normalized/verified as CRLF with BOM preservation; `git diff --check` passed. No commits or changes to other scopes were made.
