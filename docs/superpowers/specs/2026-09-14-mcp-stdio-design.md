# FO Dev Manager MCP stdio design

## Approved goal

Expose the complete existing application workflow surface to AI clients through an independent headless `fodev.exe mcp` server. WinUI need not be running. The user approved the architecture, execution model, tool contracts, and implementation on a separate branch.

## Architecture

Use the official C# ModelContextProtocol SDK, hosted by the existing .NET 10 console executable. Dispatch MCP mode before CLI parsing or console subscribers. Reserve stdout for MCP JSON-RPC traffic and send diagnostics to stderr and file logging. Use the same installed configuration and environment overrides as the CLI.

Typed, named tools call shared application operations and existing services directly. Extract reusable orchestration from CLI and desktop handlers where necessary rather than parsing console output or automating UI controls. Preserve persisted profile JSON contracts. Cover source, compiled, and compiled NuGet models, both repository-backed and standalone.

## Workflow inventory

| Area | Required headless operations |
| --- | --- |
| Profiles | List, show, create, delete with owned-link validation, import file, import repository URL, export, check/refresh, switch, get active profile |
| Profile sync | Check linked definition changes, dismiss a specific revision, reimport preserving local profile name and database |
| Models | List/show, discover/add from path, create source model, remove, update properties, read/update source version |
| Repositories | List/show, update editable properties, live Git status/health, fetch, open remote URL |
| Git workflows | Assign task with explicit branch/stash choices, open task URL, merge main, reset profile to main, tag release |
| NuGet | Add package to selected repository, list versions, update package version, remove package models coherently, prepare/restore |
| Packages | Build source or compiled package, explicit publish choice, return resulting artifact paths |
| Deployment | Inspect live ownership/targets, deploy/undeploy model or profile, undeploy all profiles |
| Solutions | Resolve path, ensure/update solution, open Visual Studio |
| Database | Read, set, apply |
| Settings | Read non-secret configuration and update supported settings; exclude credential values from responses |
| Application | Version/about, operation status |

Map pure presentation actions to their data/action equivalent; window chrome, selection, and expansion do not need tools. Document concrete tool names and their desktop counterparts in a coverage inventory.

## Execution and shared state

Serialize application operations in the MCP server. Load saved profiles afresh per operation. Use a shared cross-process lock for mutating application operations in MCP, CLI, and WinUI. Return a busy result if another operation owns it. Hold ownership until actual work completes, including synchronous services that cannot yet cancel.

Return structured operation IDs, status, data, diagnostics, and artifact paths. Tool execution failures must be MCP error results, not successful prose. Label saved-state queries versus live checks. Report that partial changes may exist if a mutating workflow fails after entering execution; do not claim rollback.

Await long-running work and send progress notifications when requested. Cancellation is cooperative: check before execution and at supported boundaries, and never report stopped while synchronous work remains active. Keep an in-memory bounded operation history queryable independently of the execution gate. Server restart loses this history; document this explicitly. A caller-supplied request ID may be used to recover the operation ID before retrying.

WinUI continues to use refresh/load paths to observe persisted external changes; no live IPC layer is introduced. Explicit tool parameters replace dialogs and pickers. Tool descriptions disclose service control, branch/stash changes, deployment changes, and publishing. Standard MCP tool annotations distinguish read-only/destructive actions. Secrets in configured credentials and URLs must not leak through results or diagnostics. Headless Git commands must not prompt through stdin or launch interactive authentication.

## Verification

Use isolated temporary stores and controlled dependencies for workflow tests. Exercise MCP initialization, tool discovery, calls, structured errors, stdout purity, progress, cancellation, status queries, and shutdown through a real child process. Verify tool inventory coverage. Check shared locking across processes and nested application workflows. Verify combined WinUI/CLI publishing and handshake with the published executable. Run existing non-local-integration tests and compare against baseline.

## Baseline and delivery decisions

- Branch: `feature/mcp-stdio`, based on `e0c0725`
- User's existing `FODevManager.WinUI/appsettings.json` edit is retained
- Baseline test run showed three missing-reflection-method failures in DeployablePackageServiceTests and two version-field expectation failures in ModelVersionServiceTests, then timed out after 120 seconds
- User explicitly selected proceeding with MCP against that baseline
- The user approved proceeding directly into implementation after the design review
- No commits, merges, or publishing to a remote feed are authorized by this task
