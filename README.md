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

The console project remains available for profile and model automation:

```powershell
fodev.exe -profile "DevProfile" <command> [options]
```

Profile commands:

| Command | Description |
| --- | --- |
| `create` | Creates a new profile |
| `import` | Imports a profile JSON file |
| `delete` | Deletes a profile |
| `check` | Validates profile paths and state |
| `list` | Lists profiles or models in a profile |
| `deploy` | Deploys all undeployed models |
| `undeploy` | Undeploys all deployed models |
| `open-vs` | Opens the active profile solution |
| `git-fetch` | Fetches latest Git state for repositories |
| `switch` | Switches to the selected profile |
| `db-set` | Sets the profile database name |
| `db-apply` | Applies the database name to FO configuration |

Model commands:

| Command | Description |
| --- | --- |
| `-model "Name" add "path"` | Adds a model to the profile |
| `-model "Name" remove` | Removes a model from the profile |
| `-model "Name" deploy` | Deploys one model |
| `-model "Name" undeploy` | Undeploys one model |
| `-model "Name" check` | Checks deployment state |
| `-model "Name" git-status` | Checks whether the model is in a Git repository |
| `-model "Name" git-open` | Opens the model repository remote URL |
| `-model "Name" peri` | Assigns task metadata |

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
dotnet test FODevManager.sln
```

The tests cover command parsing, profile handling, deployment behavior, Visual Studio solution generation, compiled NuGet package preparation, and package build reference resolution.

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
