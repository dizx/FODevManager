# MCP final review fix wave

## Result and scope

Addressed the three concrete findings on `feature/mcp-stdio` after reading the approved spec and Task 2/3 implementation reports and checking the actual shared APIs. The supplied final reviewer verdict for Task 4 is **spec pass and quality pass**. Production discovery still verifies **exactly 52 tools**.

No persisted profile/model/repository contracts, import/export formats, solution generation rules or deployment ownership rules were changed. The preflight corrections cover repository-backed and standalone deployed models, including source and compiled membership. No subagents, staging, commits or source appsettings edits were used.

## 1. Strict Git inspection and mutation preflight

- Added throwing Git inspection APIs that check command success and propagate cancellation instead of interpreting failed status as clean
- Repository status now strictly resolves branch, working-tree state, conflict/merge state, local main comparison and HEAD commit
- Strict main comparison fails when required references or fetches cannot be resolved; unavailable comparison is not a successful no-updates result
- Profile switch checks every required active repository and every target auto-checkout repository before entering UndeployAll
- Task branch assignment checks status before ChangeBranch; reset and release inspect the entire repository set before invoking their mutating service workflows
- Existing permissive Git helpers retain their default behavior for legacy callers

Regression coverage uses a temporary committed Git repository with readable HEAD, an origin configuration entry and a deliberately corrupt index. Status, switch, assignment, reset and release must fail inspection without changing the profile, branch or owned temporary symlink. The switch fixture throws if service-control preflight is reached, so no real FO services are involved. Additional checks distinguish successful clean/dirty status from a missing main reference and propagate cancellation.

## 2. Linked reimport validates before undeployment

Removed Reimport's redundant early UndeploySet and passed the caller token to `ImportFile(profile.ProfileFilePath, name, token)`. ImportFile checks cancellation before reading input, then parses and validates before its existing owned-set undeployment. Reimport continues preserving the local profile name and database.

Tests retain a real owned temporary symlink and ledger entry for missing JSON, malformed JSON, invalid model names and pre-cancelled calls. A further test cancels at the import deployment boundary and verifies cancellation propagates before link removal or profile replacement. Existing successful linked-reimport and desktop-owned service-control tests remain passing.

## 3. Package versions distinguish query failure from empty success

Added an opt-in `throwOnFailure` lookup mode and enabled it in PackageOperations.Versions. Invalid context, missing configuration/executable and failed queries now fail the operation. Strict queries require successful process exit, even when failed output includes `No packages found.`. Successful empty and populated responses remain supported. Legacy callers retain warning/empty behavior by default.

A narrow optional version-query process runner and injectable WorkflowContext package service allow deterministic tests through the real parsing, operation runner and MCP result adapter. Tests cover authentication failure, mixed no-packages/network-failure output, successful empty output and populated versions. Failed results set MCP IsError and redact the configured credential from the serialized response. No external feed is contacted.

## Red/green evidence

- Tests were added before fixes: the first Git/reimport run had 8 failures and 1 passing cancellation case
- The Git fixture was corrected to include origin, which the legacy repository detector requires. Temporarily restoring the permissive status/switch calls then reproduced the exact defects: status returned normally for a corrupt index, and switch reached controlled service preflight. Restoring strict calls made both pass
- Package tests initially failed to compile on the new injection APIs. With only the injection seam added, both failure cases reproduced successful empty results while both genuine success cases passed. Strict lookup then made all four pass
- Final regression additions total 15 cases: 11 Git/reimport cases and 4 package lookup cases

## Final verification

```powershell
dotnet test FODevManager.Tests/FODevManager.Tests.csproj --filter "TestCategory!=LocalIntegration&FullyQualifiedName!~ResolveReferencedCompiledNugetReferenceFolders&(FullyQualifiedName~HostCoordinationReviewTests|FullyQualifiedName~McpToolTests|FullyQualifiedName~McpProtocolTests|FullyQualifiedName~OperationRunnerTests|FullyQualifiedName~ApplicationOperationLockTests|FullyQualifiedName~CliHostTests|FullyQualifiedName~ProfileChangeDetectorTests|FullyQualifiedName~DeployablePackageServiceTests|FullyQualifiedName~ProfileServiceTests)" --verbosity quiet
dotnet build FODevManager.WinUI/FODevManager.WinUI.csproj -p:Platform=x64 --verbosity quiet
git diff --check
```

- Focused service/operation/protocol suite: **180 passed, 0 failed, 0 skipped**, reported duration 16 seconds
- The test command compiled the console host and shared project
- WinUI build: **0 errors, 33 existing warning diagnostics**, 16.54 seconds
- Real production child-process protocol discovery and SDK discovery still verify exactly 52 tools
- The three known missing-reflection-method package baseline cases are explicitly excluded; this is not a full-suite pass claim
- Nine wave-touched text files were normalized and verified as CRLF with BOM preservation; whitespace checks passed

Combined publishing was not repeated in this wave. Task 4's prior combined-publish validation remains documented in its report; the final validation controller can republish these updated binaries. No real FO deployment/service/database operations, external feeds or remote Git changes were executed. Mutations remain nontransactional after successful preflight.

## Wave-touched files

- `FoDevManager.Shared/Utils/GitHelper.cs`
- `FoDevManager.Shared/Operations/RepositoryOperations.cs`
- `FoDevManager.Shared/Operations/ProfileOperations.cs`
- `FoDevManager.Shared/Operations/PackageOperations.cs`
- `FoDevManager.Shared/Operations/WorkflowContext.cs`
- `FoDevManager.Shared/Services/DeployablePackageService.cs`
- `FODevManager.Tests/HostCoordinationReviewTests.cs`
- `FODevManager.Tests/McpToolTests.cs`
- `docs/superpowers/plans/mcp-final-review-report.md`

## Final independent verification

The scoped final reviewer closed all three findings with spec PASS and quality PASS, with no new critical or important regression found. The controller then rebuilt the combined output after the fix wave:

```powershell
dotnet publish FODevManager.WinUI/FODevManager.WinUI.csproj -c Release -r win-x64 --self-contained true -p:Platform=x64 -o "C:\Dev\FODevManager\Output\McpVerification" --verbosity minimal
dotnet test FODevManager.Tests/FODevManager.Tests.csproj --filter "TestCategory!=LocalIntegration&FullyQualifiedName!~Actual_Solution_Mapping_Stages_Release_X64_Content_Native_And_Own_Satellites" --logger "console;verbosity=minimal" --blame-hang --blame-hang-timeout 180s
$env:FODEV_MCP_TEST_EXECUTABLE = 'C:\Dev\FODevManager\Output\McpVerification\fodev.exe'
try {
    dotnet test FODevManager.Tests/FODevManager.Tests.csproj --no-build --filter "FullyQualifiedName~McpProtocolTests.Production" --logger "console;verbosity=normal"
} finally { Remove-Item Env:\FODEV_MCP_TEST_EXECUTABLE }
```

- Combined self-contained publish: succeeded, including updated shared and console binaries
- Broader suite: 455 executed, 450 passed, five failed, zero skipped, 35 seconds
- Failures are exactly the three previously recorded reflection-helper failures and two model-version expectation failures
- The previously identified source-packaging hang was explicitly excluded; this is not a full-suite pass claim
- Published production protocol cases: two passed, zero failed, including initialization, all 52 tools, errors/history and output/dependency checks
- No real FO deployment, database mutation, feed publishing or remote Git write was executed
