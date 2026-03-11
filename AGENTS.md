# AGENTS.md

## Project
FO Dev Manager is a WinUI 3 desktop application for managing Dynamics 365 Finance & Operations development profiles, repositories, models, deployment links, Git workflows, and Visual Studio solutions.

## Mission
Work on this codebase as a practical engineering assistant. Prefer small, safe, reviewable changes that improve correctness, maintainability, and developer workflow without changing the overall architecture unless explicitly requested.

## High-level architecture

### Main areas
- `App.xaml.cs`
  - Application startup
  - Dependency injection setup
  - Logging setup
  - Main window creation

- `MainWindow.xaml` / `MainWindow.xaml.cs`
  - Main UI shell
  - Orchestrates profile, model, deployment, repository, and Git actions
  - Contains significant code-behind logic

- View models
  - `ViewModelBase`
  - `ProfileEnvironmentViewModel`
  - `RepoGroupViewModel`
  - `ModelsGroupingViewModel`
  - `SettingsViewModel`
  - `BusyOverlayViewModel`

- Domain models
  - `ProfileModel`
  - `ProfileEnvironmentModel`
  - `RepositoryModel`
  - `ProfileModelEntry`

- Services
  - `FileService`
  - `ProfileService`
  - `ModelDeploymentService`
  - `VisualStudioSolutionService`
  - `GitHelper`
  - `BusyHandler`

## Core concepts

### Profiles
A profile represents a full FO development setup:
- profile name
- solution path
- profile artifact path
- repositories
- standalone models
- database name
- active state

Profiles are stored as JSON and can also be exported as portable profile artifacts.

### Models
Models can be:
- `Source`
- `Compiled`

A model may be:
- repository-backed
- standalone

Deployment is based on symbolic links to either:
- source metadata folders
- compiled model folders

### Repositories
A repository may contain one or more models and tracks:
- root path
- Git URL
- preferred branch
- last known branch
- commit info
- main branch
- task metadata
- stash / checkout behavior

### Solution management
The app can:
- resolve solution location
- create a solution
- add source projects
- remove source projects
- open the solution

## What the agent should optimize for
- Preserve existing FO Dev Manager workflows
- Prefer repository-first thinking over standalone-only assumptions
- Keep source and compiled model scenarios both working
- Make model/repository state transitions explicit and safe
- Avoid hidden side effects during deploy, undeploy, import, and profile switching
- Keep UI-facing logic understandable
- Favor service-level logic over growing code-behind when possible

## Important current behavior
- Profiles are persisted locally as JSON
- Profiles can be imported from file or repository URL
- Repositories may be cloned during import
- Solutions are created and updated automatically
- Model deployment uses symlinks
- Git state is monitored in the background
- External profile artifacts may be generated under an `Artifacts` folder
- Busy state and operation logging are surfaced in the UI

## Known weak points
Treat these as likely hotspots when changing code:
- Deployment status logic appears inconsistent
- `IsModelActuallyDeployed` likely has inverted logic
- `CheckModelDeployment` likely sets `IsDeployed` incorrectly in at least one path
- Repository-backed model removal may be incomplete
- Git URL parsing may be too Azure-specific
- Solution project matching may be fragile
- `MainWindow.xaml.cs` is doing too much orchestration
- Random model ID generation may collide
- Cleanup and ownership of busy overlay lifecycle should be reviewed

## Preferred change strategy
1. Understand whether the change belongs in UI, view model, or service layer.
2. Preserve persisted data contracts unless the task explicitly includes migration/versioning.
3. Keep repository-backed and standalone models both in scope.
4. Check whether source and compiled model handling both need updates.
5. Avoid coupling new logic directly into `MainWindow.xaml.cs` unless the change is purely UI-specific.
6. When changing profile import/export, consider backward compatibility.
7. When changing deployment logic, verify both actual filesystem state and model flags.
8. When changing Git workflows, assume dirty repositories and branch mismatches can happen.

## Coding conventions
- Use descriptive names
- Keep methods focused
- Prefer explicit logic over clever abstractions
- Do not introduce unnecessary new patterns
- Preserve existing naming style unless there is a clear benefit to refactor
- For user-facing logging in app/shared code, use `MessageLogger`
- Do not use `Console.WriteLine` in console app or shared files

## Logging conventions
Use:
- `MessageLogger.Info`
- `MessageLogger.Warning`
- `MessageLogger.Error`
- `MessageLogger.Highlight`

Do not introduce `Console.WriteLine` in places that follow the project convention.

## When editing code
Before making changes, identify:
- which persisted models are affected
- whether import/export format is affected
- whether source vs compiled models are both covered
- whether repository-backed vs standalone models are both covered
- whether solution generation behavior changes
- whether deployment symlink behavior changes
- whether Git operations can fail or leave partial state

## Good tasks for the agent
- Refactor service logic into smaller methods
- Fix deployment status inconsistencies
- Improve repository/model removal logic
- Improve import/export robustness
- Add support for NuGet-based compiled/ISV package installation
- Improve Git status detection and repo actions
- Reduce orchestration inside `MainWindow.xaml.cs`
- Add validation and clearer error handling
- Improve path normalization and repository URL parsing

## Avoid by default
- Large architecture rewrites
- Silent data contract changes
- UI redesigns without request
- Changing profile storage format without migration strategy
- Assuming all models are source models
- Assuming all models belong to a Git repository
- Assuming Azure DevOps is the only Git remote format

## NuGet / compiled package direction
The project is expected to evolve toward first-class support for compiled model dependencies delivered as packages. Prefer designs that allow:
- repo-level package configuration
- local install/update workflow
- coexistence of source models and compiled package models
- minimal repeated setup across repositories

## Definition of done
A change is only complete when:
- it fits the current profile/repository/model mental model
- it does not break source/compiled scenarios unnecessarily
- error handling is explicit
- logging is useful
- persisted state remains coherent
- the code is readable and maintainable
