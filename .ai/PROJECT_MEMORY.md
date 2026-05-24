# FO Dev Manager — Project Memory

## Purpose
FO Dev Manager is a WinUI 3 desktop tool for managing Dynamics 365 Finance & Operations development profiles, models, repositories, deployment links, Git workflows, and Visual Studio solutions.

It is designed to make FO local development environments easier to create, switch, maintain, and synchronize.

## Core responsibilities
- Manage **profiles** stored as JSON
- Support **repository-backed** and **standalone** models
- Support both **source** and **compiled** models
- Create and maintain **Visual Studio solution files**
- Deploy and undeploy models using **symbolic links**
- Track **Git repository health**, branches, and remote URLs
- Import/export profiles, including repository-based profile bootstrap
- Apply per-profile **database switching**
- Support task-based Git branch workflows

## Current application structure

### App startup
`App.xaml.cs`
- Configures Serilog file logging
- Registers services in DI
- Creates the main window
- Uses `AppConfig` loaded from `appsettings.json`

### Main UI
`MainWindow.xaml` / `MainWindow.xaml.cs`
- Main shell for profile and model operations
- Loads profiles into a dropdown
- Displays models grouped by repository
- Supports:
  - create profile
  - import profile from file
  - import profile from repository URL
  - create model
  - add model from folder
  - deploy / undeploy model
  - deploy / undeploy profile
  - switch profile
  - delete profile
  - open solution
  - repository and model properties
  - assign task to repository
  - open task URL
  - git reset profile
  - merge main into current branch

### View models
- `ViewModelBase`
- `ProfileEnvironmentViewModel`
- `RepoGroupViewModel`
- `ModelsGroupingViewModel`
- `SettingsViewModel`
- `BusyOverlayViewModel`

These provide UI binding, grouping, busy state, repository state, and settings persistence.

## Main domain objects

### ProfileModel
Represents a full FO development profile.

Key fields:
- `ProfileName`
- `SolutionFilePath`
- `ProfileFilePath`
- `Repositories`
- `StandaloneModels`
- `DatabaseName`
- `IsActive`

Computed helpers:
- `AllModelEntries`
- `AllModels`

### ProfileEnvironmentModel
Represents a model environment.

Key fields:
- `ModelName`
- `ModelRootFolder`
- `ProjectFilePath`
- `MetadataFolder`
- `CompiledModelFolder`
- `GitUrl`
- `IsDeployed`
- `IsMainFOModel`
- `ModelType`

`ModelType`:
- `Source`
- `Compiled`

### RepositoryModel
Represents a Git repository containing one or more models.

Key fields:
- `RepoId`
- `DisplayName`
- `RepoRootFolder`
- `GitUrl`
- `PreferredBranch`
- `LastKnownBranch`
- `LastKnownCommit`
- `MainBranchName`
- `AutoCheckoutOnProfileLoad`
- `AutoStashOnDirtyCheckout`
- `AutoApplyStashAfterCheckout`
- `Models`
- `Task`
- `TaskComment`

Repository identity is normalized and can generate a stable `RepoId`.

## Services

### FileService
Handles local profile storage.
- Loads profiles from app data JSON
- Saves profiles
- Saves external export when needed
- Deletes profiles
- Enumerates profiles

### ProfileService
Main orchestration service.
Handles:
- create profile
- switch profile
- import profile
- import profile from repo URL
- ensure repositories are built from older standalone-only data
- add model
- create model
- check profile status
- apply database
- export external profile artifact
- monitor differences between current profile and external profile file
- update repository and model properties
- git reset across profile repositories

### ModelDeploymentService
Handles model deployment and creation.
Supports:
- deploy one model
- deploy all undeployed models
- undeploy one model
- undeploy all models
- convert installed model into project model
- add model to profile if not already present
- create a new model from templates
- assign task and switch branch
- open Git remote URL

Deployment is based on symbolic links from deployment base path to:
- source model metadata folder, or
- compiled model folder

### VisualStudioSolutionService
Handles solution files.
Supports:
- resolve solution path
- create solution file
- add source projects to solution
- remove projects from solution
- open solution

Important behavior:
- prefers explicit `SolutionFilePath`
- otherwise prefers the main FO model root
- falls back to default profile folder

### GitHelper
Low-level Git utility wrapper around `git` CLI.
Supports:
- detect Git repository
- read origin URL
- get active branch and HEAD commit
- fetch, pull, checkout, stash, stash pop
- detect dirty state
- detect merge conflicts / repo attention state
- compare current branch to main
- merge main into current branch
- clone repository
- reset repository to main and update

## Profile import/export format

### Export model
`ExportProfileModel`
- `ExportFormatVersion`
- `ProfileName`
- `DatabaseName`
- `SolutionFileRelativePath`
- `Repositories`
- `StandaloneModels`

### Export repository
`ExportRepositoryModel`
- `RepoId`
- `DisplayName`
- `GitUrl`
- `AutoCheckoutOnProfileLoad`
- `AutoStashOnDirtyCheckout`
- `Models`

### Export environment
`ExportEnvironmentModel`
- `ModelName`
- `IsMainFOModel`
- `ModelType`

### Import flow
When importing:
1. Load export or legacy profile JSON
2. Clone each repository if needed
3. Configure paths for each model
4. Resolve solution path
5. Create or reuse solution
6. Add all source models to the solution
7. Save imported profile to local profile storage

### External profile artifact
If a profile has no `ProfileFilePath`, the app can bootstrap one by exporting to:
`<main FO repo root>\Artifacts\<ProfileName>.json`

This is used for profile sync checks and repository-based onboarding.

## Model detection logic
When adding a model path, the application can detect:
- installed deployed model
- compiled models under `Libs`
- source models under `Metadata`
- direct compiled model folder
- direct source model folder under `Metadata\<ModelName>`

## UI grouping logic
Models are grouped into:
- **Git repository groups**
- **Non-Git models**

`ModelsGroupingViewModel` builds a combined list used by the main list UI.

Repository groups show:
- display name
- current branch
- branch health
- whether main has updates
- task info
- contained models

## Background behavior
The main window starts background monitoring for the active profile:
- waits before first run
- periodically refreshes Git health
- periodically checks profile model sync against external profile JSON

Background checks are skipped when the app is busy or just finished a busy operation.

## Busy and logging model
- `BusyHandler` publishes busy state with operation IDs
- `BusyOverlayViewModel` subscribes and shows operation logs
- UI log preview is fed by message subscribers
- Serilog writes rolling log files under app data

## Settings currently present
`SettingsViewModel`
- `CheckUncommittedBeforeSwitch`
- `DefaultSourceDirectory`

Settings are persisted back to the same `appsettings.json`.

## Notable implemented workflows

### Switch profile
- optionally block if repositories contain uncommitted changes
- undeploy current profile
- ensure repository mapping
- optionally switch repo branches to preferred branch
- update deployment status
- deploy target profile
- apply database
- mark profile active

### Assign task to repository
- save task and comment on repository
- build branch name like `feature/task-<task>-<slug>`
- optionally auto-stash
- create or switch to branch
- persist `LastKnownBranch`

### Git reset profile
For each repository:
- stash uncommitted changes
- checkout main
- fetch and prune
- pull fast-forward only

## Architectural direction
The codebase is moving toward:
- repository-first profile structure
- cleaner separation between repository-backed and standalone models
- profile artifact export/import as a portable handoff format
- support for both source and compiled model scenarios
- richer Git-aware UI behavior in the main list

## Known weaknesses / things to revisit

### Likely bugs or inconsistencies
- `ModelDeploymentService.IsModelActuallyDeployed` appears inverted and likely incorrect
- `CheckModelDeployment` sets `IsDeployed = true` in the `else` branch when the link does not exist
- `ProfileService.RemoveModelFromProfile` removes only from `StandaloneModels`, so repository-backed models may not be removed correctly
- `ImportRepository` uses Azure DevOps repo name extraction and may be weak for non-Azure remote formats
- `VisualStudioSolutionService.AddProjectToSolution` uses name matching that could produce false positives
- `new Random()` for model ID generation may produce collisions
- `MainWindow` is large and contains a lot of orchestration that could be split further
- `MainWindow.xaml.cs` appears duplicated in uploaded context, so file duplication should be checked in the project
- Busy overlay is disposed in more than one place; harmless but worth cleaning
- `App.OnLaunched` creates `mainWindow` locally instead of assigning `App.MainWindow`

### Design cleanup opportunities
- move more orchestration from UI to application services
- separate import/export logic more cleanly
- centralize repo resolution and model removal logic
- add explicit support for package/NuGet-installed compiled models
- add validation around solution path and main FO model selection
- strengthen path normalization and non-Azure Git support

## Likely next project steps
- Add proper support for compiled/ISV packages delivered through NuGet
- Keep compiled package support in FO Dev Manager
- Add repo-level package configuration and install workflow
- Improve model removal for repository-backed models
- Fix deployment status logic bugs
- Refactor large sections of `MainWindow.xaml.cs`
- Improve branch/status UI and repository actions
- Strengthen import/export contract and versioning

## Project conventions observed
- Uses `MessageLogger` for user-facing logging
- Uses descriptive model and service names
- Uses WinUI 3 with MVVM-style view models, but code-behind still orchestrates many actions
- Uses JSON profile persistence
- Uses symbolic links for deployment
- Uses repository grouping as the main mental model for multi-model environments

## Short summary
FO Dev Manager is already a solid internal environment manager for FO development. The current codebase supports profile lifecycle, repository-aware model management, compiled/source model support, Git workflow helpers, database switching, solution generation, and external profile artifacts. The next meaningful step is to tighten repository/model consistency and add first-class package installation support for compiled dependencies.
