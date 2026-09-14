# Task 1 implementation report

## Completion

Implemented only Task 1 of `2026-09-14-mcp-stdio.md` on `feature/mcp-stdio`.

Created:

- `FoDevManager.Shared/Operations/ApplicationOperationLock.cs`
- `FoDevManager.Shared/Operations/OperationRunner.cs`
- `FoDevManager.Shared/Operations/OperationResult.cs`
- `FoDevManager.Shared/Operations/SecretRedactor.cs`
- `FODevManager.Tests/OperationRunnerTests.cs`
- This report

No subagents, commits, appsettings edits, dependency changes, or host integration were performed. The existing WinUI appsettings edit was preserved. Profile persistence/import/export contracts, source/compiled/NuGet model handling, repository/standalone handling, solution generation, deployment symlinks, and Git workflows are unaffected by this infrastructure-only task. The official MCP SDK belongs to the subsequent host task; this implementation has no protocol or stdout output.

## Public API and integration

Namespace: `FODevManager.Operations`.

```csharp
var runner = new OperationRunner(config);
var result = await runner.RunAsync(
    "probe", false,
    _ => Task.FromResult<object?>(new { value = 42 }),
    cancellationToken, requestId: "request-123", progress: progress);
var status = runner.GetStatus(result.OperationId);
var operations = runner.ListOperations();
```

An additional public constructor accepts `ApplicationOperationLock`, `historyCapacity` (default 100), and `diagnosticCapacity` (default 100). Tests inject a lock path under a unique temporary store. Production hosts should use a shared long-lived runner within each host and the default machine lock identity across hosts.

`ApplicationOperationLock.TryAcquire()` returns an `IDisposable` lease or null for contention. The default path is `CommonApplicationData/FODevManager/application-operation.lock`, independent of profile or deployment paths. Windows exclusive file sharing provides cross-process ownership and automatic release after process termination. Disposal can occur on a different async thread and is idempotent. The lock file is deliberately retained to avoid changing its identity during contention. Filesystem/access failures surface as failures, rather than being mistaken for contention or falling back to an uncoordinated lock.

`OperationResult` is a detached record snapshot with operation/request IDs, name, status, JSON data, diagnostics, dropped-diagnostic count, creation/start/completion timestamps, and `PartialChangesPossible`. Status values are `queued`, `running`, `succeeded`, `failed`, `cancelled`, and `busy`. Data is a detached `JsonElement` when non-null; diagnostics are read-only snapshot collections.

## Execution semantics

- Each runner serializes actions, including queries, through its own async gate
- Mutation leases are acquired non-blockingly after entering that gate and retained through actual execution and result serialization
- Queries do not acquire the cross-process mutation lease
- Status/list queries use a separate history monitor and never wait on the execution gate
- Cancellation before execution or during the queue wait never invokes the action and does not indicate partial changes
- Cooperative cancellation during execution is reported only after the action unwinds
- Non-cooperative successful work remains running and retains its lease despite token cancellation; it returns succeeded when it really completes
- Exceptions and captured MessageBus error messages produce failed results
- Mutation failure/cancellation after action entry conservatively indicates possible partial changes; rollback is never implied
- Lease disposal and execution-gate release use nested finally handling
- Executing operations remain running through diagnostic capture and cleanup; final status, data, completion time, and partial-change indication are published atomically under the entry lock after cleanup, including any lease-release failure
- Nested runner calls fail explicitly instead of deadlocking; compose service calls within the existing operation boundary

## Capture, history, and redaction

A single static MessageBus subscriber routes messages through an AsyncLocal execution context. This avoids per-operation subscription races and separates messages from concurrent runner instances or unrelated background work. Awaited Task.Run work inherits capture. Error state is recorded before invoking progress observers, so MessageBus swallowing observer exceptions cannot turn logged service errors into success. Synchronous progress exceptions are isolated and recorded. Diagnostics prefer retaining errors over later informational messages.

Completed history retains the configured number of results, plus all currently queued/running entries. History is memory-only and disappears with the runner/process. Request IDs provide correlation through ListOperations, not deduplication or retry guarantees.

Each operation retains at most the configured number of diagnostics, each limited to 4096 characters plus a truncation marker. Names, request IDs, and progress text use the same text limit. Both serialized input data and redacted output data have a 1 MiB limit; oversized or unserializable data fails the operation. Serialization necessarily allocates its input representation before this size check, so this is a retained-result bound rather than a streaming memory budget for arbitrary user objects.

SecretRedactor reads current AppConfig credential values, removes raw/URL-encoded configured credentials, removes URL userinfo and sensitive query values, and recursively sanitizes JSON property names, strings, arrays, and sensitive fields. It handles the actual AzureArtifactsUsername, AzureArtifactsPat, and AzureArtifactsApiKey fields. Encoded credential matching treats percent-escape hex casing as equivalent while preserving case-sensitive matching of unencoded credential characters; both URI and form-encoded variants are supported. URL query matching includes encoded names and signature/token/API-key credentials. Sensitive structured fields are replaced as a whole, including non-string values. Regexes use non-backtracking matching and a timeout; text redaction fails closed. Operation names, request IDs, exception diagnostics, service messages, progress, and returned data all pass through redaction.

## Focused test-first verification

Exact command:

```powershell
dotnet test FODevManager.Tests/FODevManager.Tests.csproj --filter FullyQualifiedName~OperationRunnerTests
```

Initial RED: the tests were created before the operation classes; the command failed with CS0234/CS0246 for the missing Operations namespace, OperationRunner, and ApplicationOperationLock.

Intermediate focused runs identified a test fixture that inadvertently classified an entire nested object as secret, a result-size/redaction-timeout interaction, and loss of failure diagnostics under later informational traffic. The fixture was corrected, result bounds and regex matching were strengthened, and error-priority retention was implemented. The diagnostic-retention assertion was observed failing before its implementation fix.

Initial implementation GREEN: **14 passed, 0 failed, 0 skipped**, .NET 10, reported test duration 229 ms. Review-fix verification below supersedes this count. Only the focused suite was executed. Existing unrelated nullable/platform warnings appeared during compilation. The five known unrelated baseline failures were not rerun.

Tests:

1. `SerializesAndExposesWaitingAndRunningStatus`
2. `BusyLeaseDoesNotExecuteAndReleasesAcrossAsyncThreads`
3. `CancellationBeforeAndWhileWaitingNeverExecutes`
4. `NonCooperativeWorkKeepsLeaseAndRunningStatusUntilItFinishes`
5. `CapturesErrorsEvenWithThrowingProgressAndBoundsDiagnostics`
6. `ExceptionsCancellationAndSerializationFailuresReleaseLease`
7. `HistoryIsBoundedAndSnapshotsAreDetached`
8. `RedactsConfiguredAndUrlSecretsInNestedDataAndPropertyNames`
9. `LockContendsAcrossProcessesAndRecoversAfterOwnerExit`
10. `QueriesDoNotAcquireMutationLeaseAndCaptureIsExecutionScoped`
11. `DiagnosticsAndDataAreBoundedAndProgressIsRedacted`
12. `InvalidLockPathFailsExplicitlyWithoutExecuting`
13. `NestedRunnerFailsWithoutDeadlockingAndOuterLeaseIsReleased`
14. `RedactorUsesCurrentConfigAndEncodedCredentials`

The cross-process test starts Windows PowerShell holding the same exclusive file, verifies runner busy behavior, kills the child, and verifies recovery. Test resources are isolated temporary directories and synchronization primitives; no FO installation, profiles, or deployment resources are used.

## Task 1 review fixes

Both requested review issues were reproduced with tests before implementation changes:

1. Terminal status was previously assigned before cleanup and Finish. Concurrent status queries could observe a terminal result without its completion time or partial-change flag, and a mutation that logged an error could transiently appear succeeded. RunAsync now retains a private provisional outcome and data; Entry.Finish alone publishes terminal state atomically, after capture closes and lease/gate cleanup completes. Lease-release exceptions update the provisional outcome and diagnostics before publication
2. Exact encoded-credential replacement missed lowercase/mixed percent-escape hex digits. Encoded variants now use escaped literal regex patterns with case-insensitive groups limited to percent escapes. Other credential characters remain case-sensitive, and raw credentials still use ordinal replacement

Added regression cases:

- `StatusPollingNeverObservesIncompleteOrIncorrectTerminalSnapshot`: four cases covering success, logged errors, exceptions, and cooperative cancellation. Each polls both ListOperations and GetStatus concurrently across 300 mutations and rejects incomplete terminal snapshots, incorrect partial-change flags, and transient success for failing mutations
- `StatusRemainsRunningWhileFailureDiagnosticsAreCaptured`: deterministic status observation from an exception Message getter verifies that failure diagnostic capture still exposes running, followed by a complete failed result
- `RedactsEquivalentPercentEscapeCasingWithoutChangingLiteralCase`: three lowercase/mixed-hex URI/form encoding cases, each exercised in ordinary text, URL paths, and non-sensitive query values, including structured data. Negative assertions verify that changing an unencoded credential letter's case is not treated as an equivalent credential

Review RED: **8 failed, 14 passed** using the exact focused command above. All eight new cases reproduced the old behavior.

Review GREEN: **22 passed, 0 failed, 0 skipped**, .NET 10, reported test duration **413 ms**, using the same command. No broader suite was run. Existing unrelated compilation warnings remain. Changes for this review are limited to OperationRunner.cs, SecretRedactor.cs, OperationRunnerTests.cs, and this report; public signatures and host integration scope are unchanged.

## Integration concerns

- The machine lock directory must be accessible to the accounts running supported hosts. This task does not provision cross-account ACLs; access denial fails closed. Different-user/elevation combinations still need installation/integration validation
- Hosts must share the default lock identity and acquire it at outer mutation boundaries; actual CLI/WinUI wiring remains Task 3
- Actions must await their real work. Fire-and-forget work or work explicitly suppressing ExecutionContext cannot be reliably attributed/cancelled by this runner; messages after capture closes are ignored
- Existing MessageBus subscribers still receive original messages. MCP host stderr/file subscribers must independently apply redaction and keep stdout protocol-only
- IProgress.Report is called synchronously; exceptions thrown directly by Report are contained, but exceptions raised later by an asynchronously dispatching Progress<T> callback belong to that callback's synchronization context. Host progress adapters should handle their own asynchronous failures
- A synchronously blocking action may occupy its caller thread until it reaches an await or finishes. Status APIs remain independent, but hosts must dispatch such work appropriately to keep UI/protocol request handling responsive
- Redaction deliberately treats credential-like field names conservatively, so some non-secret values with those names may also be replaced
- Completed history is bounded; outstanding queued/running requests remain visible and are not evicted. Host request admission limits, if needed, belong at the host boundary

Touched text files were normalized to CRLF and checked for stray line endings. Whitespace and final workspace status were reviewed without staging or committing.
