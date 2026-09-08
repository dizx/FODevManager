# FO Dev Manager

FO Dev Manager is a Windows desktop utility for managing Dynamics 365 Finance and Operations development profiles, repositories, models, deployment links, Git workflows, Visual Studio solutions, and compiled NuGet package builds.

The primary experience is the WinUI 3 desktop app. A console entry point is still available for profile and model operations.

## Current Version

- App version: `1.1.5`
- Runtime: `.NET 10`
- UI: WinUI 3, Windows App SDK
- Platform: Windows x64

## What It Manages

FO Dev Manager keeps a complete FO development setup in a profile:

- Profile name, active state, database name, and solution path
- Git repositories and repository-level settings
- Source models under repository `Metadata` and `Project` folders
- Compiled models and compiled NuGet model dependencies
- Deployment state for symbolic links into `PackagesLocalDirectory`
- Task metadata, Git branch information, and repository health state

Profiles are stored locally as JSON and can also be imported from repository artifacts.

## Key Features

### Profile Management

- Create, delete, switch, import, and export developer profiles
- Store profile artifacts in repositories for repeatable team setup
- Import profiles directly from a Git repository
- Detect model changes in repo-stored profile artifacts and prompt for re-import
- Apply the profile database name to FO `web.config`

### Repository-Aware Model Management

- Group models by Git repository in the UI
- Track repository root, Git URL, preferred branch, main branch, and last known branch
- Support source, compiled, and compiled NuGet model types
- Keep repository-backed and standalone models in the same profile
- Assign task metadata at repository level

### Deployment Automation

- Deploy and undeploy profile models by creating or removing links under `PackagesLocalDirectory`
- Check deployment status against actual filesystem state
- Prepare compiled NuGet model folders before deployment or package builds
- Keep profile deployment flags aligned with the detected state

### Git Workflows

- Fetch and inspect repository status from the active profile
- Switch repositories to task branches with optional auto-stash behavior
- Merge the configured main branch into the active branch when needed
- Reset a profile safely by stashing changes, checking out main, fetching, and fast-forward pulling
- Open repository URLs from the UI

### Visual Studio Solution Management

- Resolve the solution path from the active profile
- Prefer the main FO model repository for solution ownership
- Create or reuse solutions when importing or switching profiles
- Add source model projects without duplicating model entries
- Open the active profile solution from the app

### Compiled NuGet Models

- Add or update compiled NuGet model references from package URLs
- Track package ID, version, source URL, and extracted compiled model folder
- Download private package feed dependencies using configured Azure Artifacts credentials
- Use `Build\isv.config` to resolve compiled NuGet package references per repository
- Include only descriptor-referenced compiled NuGet models when building a source model package

### Package Build Workflow

- Build source FO models into compiled NuGet artifacts
- Automatically replace DLL `HintPath` references with `ProjectReference` entries when a unique matching C# project is found within the model repository/root
- Rebuild referenced C# projects with the solution and include their runtime dependencies in the model package
- Package existing compiled models without requiring a source project or compilation
- Restore FO build packages and compiler packages through NuGet
- Include standard FO references, compiled NuGet references, and model-specific binary dependencies
- Generate package build solutions for source models
- Optionally push the created compiled NuGet package to a configured feed
- Log build diagnostics and NuGet failures with actionable messages

### Settings And Installer Support

- Configure Azure Artifacts username, PAT, and API key for private feeds
- Configure a custom `nuget.exe` path when it is not available on `PATH`
- Configure automatic package push behavior after package builds
- Preserve existing installed `appsettings.json` during installer upgrades
- Enable long path support in the app manifest

## Prerequisites

- Windows 10 or Windows 11 x64
- Developer Mode enabled for link creation workflows
- Visual Studio 2022 with Dynamics 365 FO development tooling
- .NET 10 SDK
- Git CLI
- `nuget.exe` on `PATH` or configured in Settings
- Access to any private Azure Artifacts feeds used by repository `nuget.config` or `Build\isv.config`

## Build And Run

From the repository root:

```powershell
dotnet build FODevManager.sln
dotnet publish FODevManager.WinUI\FODevManager.WinUI.csproj -c Release -r win-x64 --self-contained true
```

Run the desktop app from the publish output:

```powershell
FODevManager.WinUI.exe
```

The Inno Setup script builds the installer from the WinUI publish folder:

```text
innosetup.iss
```

## Configuration

The WinUI app reads `FODevManager.WinUI\appsettings.json`. Installed applications keep their own copy under the install directory.

```json
{
  "ProfileStoragePath": "%APPDATA%\\FODevManager",
  "DeploymentBasePath": "C:\\AOSService\\PackagesLocalDirectory",
  "DefaultSourceDirectory": "C:\\Dev\\FO",
  "DeployablePackages": "%APPDATA%\\FODevManager\\DeployablePackages",
  "ModelIdBegin": 896001001,
  "ModelIdEnd": 896009999,
  "TaskUrl": "https://tasks.peritus.no/nb-NO/Case/Details/",
  "CheckUncommittedBeforeSwitch": false,
  "AzureArtifactsUsername": "",
  "AzureArtifactsPat": "",
  "AzureArtifactsApiKey": "",
  "NuGetExecutablePath": "",
  "PushDeployablePackageOnBuild": false,
  "PushDeployablePackageSource": ""
}
```

Important settings:

- `ProfileStoragePath`: local profile JSON and app data root
- `DeploymentBasePath`: FO `PackagesLocalDirectory`
- `DefaultSourceDirectory`: default source repository root
- `DeployablePackages`: extracted compiled NuGet package storage
- `NuGetExecutablePath`: optional explicit path to `nuget.exe`
- `PushDeployablePackageOnBuild`: push compiled NuGet packages immediately after build
- `PushDeployablePackageSource`: feed name or source used when pushing packages

## Console Usage

Build or publish the console separately from the WinUI installer:

```powershell
dotnet build FODevManager\FODevManager.csproj -c Release
dotnet publish FODevManager\FODevManager.csproj -c Release -r win-x64 --self-contained true
```

Run `fodev.exe` from the console output directory. Use `help`, `--help`, or `help <command>` for usage. Commands and option names are case-insensitive; profile names, model names, and paths retain their casing. Both `-profile` / `--profile` and `-model` / `--model` are accepted.

```powershell
fodev.exe -profile "DevProfile" <command> [options]
```

Profile commands:

| Command | Description |
| --- | --- |
| `create` | Creates a new profile |
| `import` | Imports a profile JSON file |
| `delete` | Undeploys verified owned links and deletes the local profile; preserves solutions, repositories, and exported artifacts |
| `check` | Validates and saves profile state; may clone repositories and update external artifacts |
| `list` | Lists profiles or models in a profile |
| `show` | Shows saved profile and model details without refreshing repositories or saving the profile |
| `repos` | Lists repositories, paths, and saved branch information |
| `export "output.json"` | Writes a portable profile artifact; refuses to overwrite an existing file |
| `solution-path` | Reports the resolved solution path |
| `solution-ensure` | Creates or updates the solution with source model projects |
| `deploy` | Deploys all undeployed models |
| `undeploy` | Undeploys all deployed models |
| `open-vs` | Opens the active profile solution |
| `git-fetch` | Fetches latest Git state for repositories |
| `switch` | Switches to the selected profile |
| `db-set "DatabaseName"` | Saves the database name and applies it immediately if the profile is active |
| `db-apply` | Applies the database name to FO configuration |

Model commands:

| Command | Description |
| --- | --- |
| `-model "Name" add "path"` | Adds a model to the profile |
| `-model "Name" remove` | Undeploys a verified owned link before removing membership; compiled NuGet package removal is refused |
| `-model "Name" show` | Shows saved details for one model |
| `-model "Name" deploy` | Deploys one model |
| `-model "Name" undeploy` | Undeploys one model |
| `-model "Name" check` | Checks deployment state |
| `-model "Name" git-status` | Checks whether the model is in a Git repository |
| `-model "Name" git-open` | Opens the model repository remote URL |
| `-model "Name" peri "Task1234"` | Saves task metadata without stashing or checking out a branch |
| `-model "Name" package-build` | Rebuilds source models and their C# projects, or packages existing compiled models; always produces NuGet locally and never publishes |

Examples:

```powershell
fodev.exe list
fodev.exe import "C:\Artifacts\TeamProfile.json"
fodev.exe -profile "DevProfile" show
fodev.exe -profile "DevProfile" repos
fodev.exe -profile "DevProfile" export "C:\Artifacts\DevProfile.json"
fodev.exe -profile "DevProfile" solution-ensure
fodev.exe -profile "DevProfile" -model "MyModel" package-build
```

Legacy `-profile list`, `-profile import "file.json"`, and name-inferred `-model add "path"` remain supported. `git-check` is an alias for `git-status`; this checks repository membership, not a full working-tree status report.

For source packaging, reference conversion is an automatic, persisted update to the `.rnrproj` during build preparation. Matching C# projects are discovered along the DLL hint path within the repository/model root; ambiguous matches fail instead of guessing, and vendor DLL references without a matching project remain file references. Repeated builds do not add duplicate project references. Generated package solutions include the required build configuration and referenced projects without rewriting the original solution.

Source packaging requires new compiler output from the current build; it never falls back to an older deployed model after a failed or skipped compilation. Runtime dependencies are collected from the actual build, and reference-only assemblies are excluded from the final package. Already compiled models use their configured compiled folder, including its supporting libraries. Repackaging an installed `CompiledNuget` model is not supported; use the original package instead.

On Windows, source builds temporarily map the build-package cache to an unused drive letter (Z through D). This keeps Microsoft FO compiler reference paths short even when Windows long paths are enabled. The cache is not moved, settings are unchanged, and the exact mapping is removed after compilation, including on build failure. Builds require an available drive letter. Abrupt process termination can leave a mapping until logoff; the build log identifies its letter and target so it can be checked before manual removal. This workaround shortens build-cache paths, not arbitrary source or artifact paths.

The CLI loads optional `appsettings.json` and environment-specific JSON from its executable directory, then applies environment-variable overrides. It does not automatically read the WinUI application's settings. For example, set `$env:ProfileStoragePath` to select a separate profile store. Keep credentials in configuration or environment variables rather than command arguments.

Exit codes are `0` for success/help, `2` for invalid usage, and `1` for operational failure. Error messages go to stderr; human-readable output goes to stdout. `show`, `repos`, and `list` report saved state, not live Git or deployment health, and do not rewrite profiles or artifacts. Initialization may create the configured profile directory and logs.

Deployment, database, and switching operations require a suitable FO machine and service-control permissions. `switch` can stash work, change branches, restore packages, replace deployments, and apply a database. These shared workflows are not transactional: a failure exit code does not imply rollback. Active `db-set` saves before application, so an application failure can leave the requested name persisted. Service-controlled workflows may start W3SVC even if it was initially stopped. Avoid concurrent CLI/UI mutations of the same profile or deployment paths.

CLI undeploy/delete/remove refuse unmanaged folders, broken links, foreign ownership, and mismatched deployment targets rather than forcing deletion. Repository-URL import, package add/update/removal, and destructive Git reset/merge workflows remain desktop operations. Package builds need the same FO tools, feed access, and repository build configuration as the desktop workflow.

## Project Structure

```text
FODevManager/
  FODevManager/                 Console entry point
  FODevManager.WinUI/           WinUI 3 desktop app
  FoDevManager.Shared/          Models, services, config, logging, Git and file helpers
  FODevManager.Tests/           NUnit tests for services and workflows
  docs/superpowers/             Design specs and implementation plans
  innosetup.iss                 Installer definition
```

Important service areas:

- `ProfileService`: profile lifecycle, import/export, repository/model orchestration
- `ModelDeploymentService`: deployment and undeployment link handling
- `DeployablePackageService`: compiled NuGet package install, reference resolution, and package builds
- `VisualStudioSolutionService`: solution creation, project discovery, and package build solution generation
- `GitHelper`: Git command execution and repository state helpers

## Tests

Run the test suite with:

```powershell
dotnet test FODevManager.Tests\FODevManager.Tests.csproj --filter "TestCategory!=LocalIntegration"
```

The tests cover command parsing, profile handling, deployment behavior, Visual Studio solution generation, compiled NuGet package preparation, and package build reference resolution.

`LocalIntegration` tests require a configured developer machine and perform real package-build workflows; run them explicitly only in that environment.

## Current Roadmap

- Improve deployment status consistency and symlink validation
- Continue reducing orchestration in `MainWindow.xaml.cs`
- Expand compiled NuGet package install and update workflows
- Improve Git remote parsing beyond Azure DevOps-specific assumptions
- Add more service-level tests for profile switching, import/export, and package workflows

## Contributing

- Prefer small, focused changes that preserve existing profile and repository workflows
- Keep source models, compiled models, and compiled NuGet models in scope when changing model logic
- Preserve persisted profile JSON contracts unless a migration strategy is included
- Use `MessageLogger` for app and shared-service logging

## License

Licensed under the MIT License.
