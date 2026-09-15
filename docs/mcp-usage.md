# Using FO Dev Manager with AI

This guide connects an AI client to FO Dev Manager so you can manage your FO development environment using natural-language requests.

FO Dev Manager provides **52 MCP tools** for profiles, models, Git repositories, NuGet packages, deployment, solutions, databases, and settings. The AI client launches `fodev.exe mcp` and calls those tools for you. **The WinUI application does not need to be open.**

For every available tool and detailed execution behavior, see the [MCP reference](mcp.md).

## 1. Locate the executable

Use `fodev.exe` from a published or installed build that includes MCP support. Keep the executable with its accompanying DLLs and configuration files.

For the development build in this repository, the path is:

```text
C:\Dev\FODevManager\Output\McpVerification\fodev.exe
```

For an installed build, use the actual installation path, for example:

```text
C:\Program Files\FODevManager\fodev.exe
```

You can check the executable in PowerShell:

```powershell
& "C:\Dev\FODevManager\Output\McpVerification\fodev.exe" help
```

The help output should mention `fodev.exe mcp`.

To create the combined desktop/console build yourself, run this from the repository root with the .NET 10 SDK installed:

```powershell
dotnet publish FODevManager.WinUI/FODevManager.WinUI.csproj -c Release -r win-x64 --self-contained true -p:Platform=x64 -o Output/McpVerification
```

## 2. Connect your AI client

### OpenCode

Merge this `mcp.fodev` entry into your OpenCode configuration. Use a project-level `opencode.json` for that project, or your global OpenCode configuration to make it available across projects. Preserve any other existing configuration entries.

```json
{
  "$schema": "https://opencode.ai/config.json",
  "mcp": {
    "fodev": {
      "type": "local",
      "command": [
        "C:\\Dev\\FODevManager\\Output\\McpVerification\\fodev.exe",
        "mcp"
      ],
      "enabled": true
    }
  }
}
```

Replace the executable path if you use an installed build. Restart OpenCode after changing the configuration, then check the connection:

```powershell
opencode mcp list
```

The `fodev` server should appear as connected. Its tools become available to the AI; OpenCode may display them with a `fodev_` prefix.

### Clients using `mcpServers`

For clients that accept the common `mcpServers` configuration format, merge this entry into their MCP settings:

```json
{
  "mcpServers": {
    "fodev": {
      "command": "C:\\Dev\\FODevManager\\Output\\McpVerification\\fodev.exe",
      "args": ["mcp"]
    }
  }
}
```

The location of this configuration depends on the client. Restart or reconnect the server using that client's MCP controls.

**Let the client launch the process.** You do not need to start it in another terminal. Running `fodev.exe mcp` manually waits for protocol messages; it is not an interactive command prompt and does not open a browser or network port.

## 3. Check the connection and configuration

Start with this prompt:

> Use the fodev MCP tools to show the application version, effective settings, and available profiles.

The AI should use `application_about`, `settings_read`, and `profiles_list`.

Check these settings before selecting a workflow:

| Setting | What it controls |
| --- | --- |
| `ProfileStoragePath` | Where FO Dev Manager reads and saves profiles |
| `DeploymentBasePath` | The FO PackagesLocalDirectory deployment destination |
| `DefaultSourceDirectory` | Where new source models and repository workflows place files |
| `DeployablePackages` | Storage for extracted compiled packages |
| `NuGetExecutablePath` | Explicit nuget.exe path, if it is not on PATH |

The server loads settings beside `fodev.exe`, then environment-specific settings, then environment-variable overrides. Its working directory does not select its configuration. A separately published console can therefore use different settings from your desktop installation.

To select a specific profile store in OpenCode, add `environment` inside the `fodev` entry:

```json
"environment": {
  "ProfileStoragePath": "C:\\FODevData\\Profiles"
}
```

Clients using `mcpServers` commonly call this field `env` instead. Point it at your existing profile folder to see existing profiles; an empty folder gives an empty list. A different profile store alone does not isolate deployment or database operations from your FO environment.

Supported changes made through `settings_update` take effect for subsequent MCP calls. External edits to settings files require restarting the MCP server. Environment overrides take precedence over saved values.

## 4. Ask the AI to perform a workflow

Replace `DevProfile`, `MyModel`, and other example values with your actual names. Include the profile name in requests so the intended target is clear. The AI can discover repository IDs through `repositories_list`.

### Inspect an environment

> Use fodev to show the models and repositories in DevProfile. Check actual deployment links and live local Git status. Summarize anything that needs attention.

Typical tools: `profile_show`, `models_list`, `repositories_list`, `deployment_inspect`, `repository_status`.

Saved profile details may contain old deployment or branch flags. Live inspection tools check the filesystem and local Git repository. To include remote changes, explicitly ask the AI to fetch first.

### Fetch and inspect Git changes

> Fetch the repositories in DevProfile, then show their current branches, uncommitted changes, and whether main has updates.

Typical tools: `git_fetch`, `repository_status`.

### Import a profile

> Import C:\Artifacts\TeamProfile.json as DevProfile using fodev, then show the imported models and resolved solution path.

Typical tools: `profile_import_file`, `profile_show`, `solution_path`.

For a repository-based definition, provide the repository URL and ask for `profile_import_repository`. Imports can clone repositories, restore packages, and create or update solutions.

### Deploy or switch profiles

> Deploy MyModel from DevProfile, then verify its actual deployment target.

Typical tools: `deployment_deploy`, `deployment_inspect`.

> Switch the FO environment to DevProfile and report the resulting active profile and deployment state.

Typical tools: `profile_switch`, `profile_active`, `deployment_inspect`.

Switching can undeploy the previous environment, change branches and stashes, restore packages, deploy models, and apply the profile database. These actions use your local FO machine and its service permissions.

### Build a package locally

> Build a compiled NuGet package for MyModel in DevProfile. Set publish to false and return the generated artifact paths.

Tool: `package_build` with `publish: false`.

Source models are compiled; existing compiled models are packaged from their configured payload. Repackaging an installed compiled NuGet model is unsupported. For publishing, explicitly request `publish: true` and configure the target feed and credentials first.

### Update a NuGet dependency

> List the available versions of Contoso.Models in the Contoso repository in DevProfile.

> Update that package to version 2.3.0, then show the resulting package models and deployment state.

Typical tools: `repositories_list`, `nuget_versions`, `nuget_update`, `models_list`, `deployment_inspect`.

NuGet add/update/restore uses repository package configuration. Removing one package can remove multiple models belonging to that package. Package addition uses the existing supported Azure Artifacts package overview URL format.

### Work on a task branch

> Assign task Task1234 to the Contoso repository in DevProfile. Switch to feature/Task1234, create it if needed, and stash existing changes before switching.

Tool: `git_assign_task`, with explicit branch and stash choices.

### Review linked profile changes

> Check whether DevProfile's linked profile definition changed and summarize the differences.

> Reimport that linked definition while preserving my local profile name and database.

Typical tools: `profile_sync_check`, `profile_sync_reimport`. You can instead ask to dismiss the returned definition revision with `profile_sync_dismiss`.

### Open the solution

> Ensure the solution for DevProfile includes its source projects, then open it in Visual Studio.

Typical tools: `solution_ensure`, `solution_open`.

### Change a database

> Set the database for DevProfile to AXDB_Dev.

Tool: `database_set`. If the profile is active, this also applies the database to the FO configuration. For an inactive profile, the saved name can be applied later with `database_apply` or when switching profiles.

## 5. Follow long-running operations

Builds, imports, and package restoration can take time. Clients that request progress receive progress messages. The server returns an `operationId`, status, diagnostics, and result data.

You can include a correlation ID in a request:

> Build MyModel in DevProfile locally with publish false and requestId build-mymodel-001.

If your client times out or stops waiting:

> Use operation_status to check requestId build-mymodel-001 before starting another build.

Operation history is available only in the same running MCP server process. Restarting it clears history. Reusing a request ID does not prevent duplicate execution.

Cancellation is cooperative. Some underlying work continues until it reaches a supported stopping point; cancellation does not undo completed changes. A failed mutation can report `partialChangesPossible`, so inspect its diagnostics and current state before retrying.

## 6. Troubleshooting

| Symptom | What to check |
| --- | --- |
| Server cannot start | Confirm the absolute executable path, the mcp argument, and that the full published folder is present |
| No profiles appear | Ask for settings_read and check ProfileStoragePath and the Windows account running the client |
| Terminal appears to hang after starting mcp | The server is waiting for an MCP client; configure the client to launch it |
| Server reports busy | Another CLI, WinUI, or MCP mutation holds the shared operation lock; let that operation finish |
| Access denied for the operation lock | The client account needs access to %ProgramData%\FODevManager\application-operation.lock and its directory |
| Deployment or database action fails | Check FO paths, symlink privileges, W3SVC/service-control access, and web.config permissions |
| Git authentication fails | Configure noninteractive Git authentication for the same Windows account; MCP disables interactive credential prompts |
| NuGet lookup, restore, or build fails | Check NuGetExecutablePath/PATH, repository nuget.config and Build/isv.config, feed access, and configured credentials |
| Settings update has no visible effect | An environment variable may override the saved value; inspect effective settings |
| MCP protocol/JSON parsing errors | Launch fodev.exe directly; shell banners or other text written to stdout break the protocol |
| Desktop does not immediately show an MCP change | Refresh or reload the profile; there is no live desktop IPC connection |

Logs are written to:

```text
%APPDATA%\FODevManager\Logs\fodev-mcp-*.log
```

Diagnostics also go to the MCP process's stderr, which many clients expose in their server logs.

Store feed credentials in FO Dev Manager configuration or environment variables rather than chat prompts. Settings queries expose a credential-presence indicator, not credential values. Published builds clear credentials from their output configuration, so configure feed access for the account and installation you use.

For release automation, note that `git_tag_release` performs descriptor commits and remote tag pushes. This is separate from the `publish` option on package builds.

## More information

- [Complete tool reference and execution semantics](mcp.md)
- [Project setup, configuration, and CLI usage](../README.md)
- [OpenCode MCP configuration documentation](https://opencode.ai/docs/mcp-servers/)
