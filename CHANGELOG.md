# FO Dev Manager Changelog

## [1.1.5] - 2026-05-24

### Added
- Compiled NuGet model dependencies are now resolved during source model package builds.
- Source model descriptors are read from `Metadata\<ModelName>\Descriptor\<ModelName>.xml` to determine `ModuleReferences`.
- Repository `Build\isv.config` entries are used to match descriptor references to exact compiled NuGet package IDs and versions.
- Package builds now stage referenced compiled NuGet models under a dedicated build reference root and include their `bin` folders when present.

### Changed
- Package builds include only compiled NuGet models that are explicitly referenced by the source model descriptor and repository `isv.config`.
- Standard FO build references continue to be resolved through the existing FO build package workflow.
- Build diagnostics now clearly identify missing descriptors, missing `isv.config`, unresolved package references, and missing compiled model folders.

### Fixed
- Fixed source model package builds that failed when a model referenced a compiled NuGet dependency such as `CarModel`.
- Prevented unrelated compiled NuGet models in the profile from being added to every package build.

## [1.1.4] - 2026-05-24

### Added
- Added stronger package build diagnostics around NuGet restore, compiler package resolution, and MSBuild reference folders.
- Added tests for package build reference resolution and compiled NuGet dependency selection.

### Changed
- Improved deployable package build reliability by preparing model-specific build folders more deterministically.
- Improved NuGet restore failure messages so authentication, feed connectivity, rejected arguments, and missing tools are easier to diagnose.

## [1.1.3] - 2026-05-24

### Added
- Added package build solution support for source model builds.
- Added a default WinUI `nuget.config` to support package restore behavior.
- Added configurable NuGet executable discovery through app settings.

### Changed
- Visual Studio solution management now creates package build solutions for source models that need compiled NuGet artifacts.
- Project discovery and solution generation now handle model build projects more reliably.

## [1.1.2] - 2026-05-24

### Added
- Enabled Windows long path support in the application manifest.

### Changed
- Promoted the WinUI app and installer version to `1.1.2`.

## [1.1.1] - 2026-05-24

### Added
- Added `NuGetExecutablePath` configuration so users can point FO Dev Manager at a specific `nuget.exe`.
- Added settings for automatic compiled NuGet package push behavior after a successful build.

### Changed
- NuGet discovery now checks the configured executable path before searching common install locations and `PATH`.

## [1.1.0] - 2026-05-24

### Added
- Added first-class compiled NuGet model handling.
- Added package URL tracking and navigation for compiled NuGet model references.
- Added Azure Artifacts credential settings for private package feeds.
- Added app settings migration support for new configuration keys.

### Changed
- Renamed user-facing deployable package terminology toward **Compiled Nuget** terminology.
- Profile import and startup now prepare compiled NuGet model folders when needed.
- Installer upgrades now preserve existing `appsettings.json` while refreshing installed application files.

### Fixed
- Improved startup diagnostics when configuration values are missing or invalid.
- Reduced package install failures caused by stale or missing local compiled NuGet folders.

## [1.0.0] – 2025-12-XX

🎉 First stable release

The focus of 1.0 is safe Git workflows, profile/repository alignment, and deterministic solution & model handling across multi-repo setups.

## ✨ Major Features

### 🧠 Main FO Model & Solution Ownership
- Introduced **`IsMainFOModel`** to explicitly mark the primary FO model in a profile.
- Visual Studio solution files (`.sln`) are now:
  - Created **inside the main FO model repository**
  - Reused automatically if an existing solution already exists

We now have a new three level profile files: Profile, Repository and Model - to better integrate with GIT during development.
Also a brand new export format, that removes all filepaths that causing issues between different environments

### 🔀 Git Workflows (Repo & Profile Level)

#### Merge main into current branch
- Detects when `origin/<main>` has new commits.
- Prompts the user to **merge main into the active branch**.
- Automatically fetches before merging.
- Re-runs repository health checks after merge.
- Merge conflicts are surfaced clearly and require manual resolution.

#### Safe Git reset (profile-wide)
- New **“Git reset profile”** action across all repositories.
- For each repo:
  - Stashes uncommitted changes (including untracked files).
  - Checks out the configured main branch.
  - Fetches latest changes.
  - Updates using **fast-forward-only pull**.
- This is **not** a destructive `reset --hard`; it is a safe recovery/update workflow.

#### Improved Git safety
- Optional blocking of profile switching if uncommitted changes exist.
- Branch switching supports:
  - Auto-stash
  - Configurable behavior per repository
- Active branch and remote URL tracking is stable and cached.

---

### 📥 Import & Sync from Repository

#### Import profile from repository
- Profiles can be imported directly from a Git repository.
- Flow:
  - Clone (or reuse) repository.
  - Locate profile JSON under `Artifact/` or `Artifacts/`.
  - Import and configure profile automatically.
- Imported profiles behave exactly like local profiles afterward.

#### Profile ↔ repo synchronization
- Background detection of changes in repo-stored profile files:
  - Added models
  - Removed models
- User is prompted to **re-import profile** when differences are detected.
- Runs shortly after profile activation and periodically while active.
- Introduced `ModelSyncResult` to keep sync logic UI-independent.

### 📦 Model Handling Improvements

#### Compiled model support
- Full support for compiled (non-source) FO models:
  - Auto-detected under `Libs/<ModelName>` via `.xref`.
- Compiled and source models can coexist in the same repository.
- New `ModelType` (`Source`, `Compiled`) on environments.
- Compiled models are visually distinguished in the UI.

#### Create & convert models
- Create FO models from scratch:
  - Full folder structure
  - `.rnrproj`
  - Descriptor XML with valid model ID
- Convert installed models from `PackagesLocalDirectory` into project models.
- Automatically cleans up installed model after conversion.

### 📂 Repository-Aware Profiles
- Models are grouped by Git repository automatically.
- Repository metadata tracked per group:
  - Repo root
  - Git URL
  - Preferred branch
  - Auto-checkout & auto-stash settings
- Repo-level Task support (external task system):
  - One Task ID/comment applied across all models in the repo.

### 🔄 Profile Switching (Deterministic)
Switching profiles now performs a **fully ordered, safe sequence**:
1. Optional Git dirty check (abort if configured).
2. Undeploy current profile’s models.
3. Switch Git branches per repository.
4. Update deployment status.
5. Deploy required models.
6. Apply profile database to `web.config`.
7. Mark new profile as active.

---

## 🧰 Internal & Technical Improvements
- All output routed through `MessageLogger`.
- Clean separation between service, UI, and shared layers.
- Deep-clone support for profiles and environments.
- Centralized helpers for Git, file discovery, config, and database switching.

---

## 🧹 Fixes & Stability
- Fixed duplicate project entries in `.sln` files.
- Eliminated solution drift across profiles.
- Improved Git error handling and messaging.
- Deployment state always reflects actual filesystem state.


## [0.9.2] – 2025-11-19

### ✨ Added
- **Main FO model–aware solution location**
  - Visual Studio solution files are now stored in the main FO model repo instead of under the generic profile folder.
  - The main repo is determined by `ProfileEnvironmentModel.IsMainFOModel`. If set, the `.sln` is created directly under that model’s `ModelRootFolder`.
  - `ProfileModel.SolutionFilePath` is now respected as the authoritative solution location when present.

- **Profile → repo sync check (model changes)**
  - New background check compares the active profile’s environments with the profile JSON in the repo (`ProfileFilePath`).
  - When models (environments) are **added or removed** in the repo definition, FO Dev Manager detects the change and can prompt the user to re-import the profile.
  - The check runs:
    - ~10 seconds after a profile is activated.
    - Then every 5 minutes for the active profile (until the profile is changed).

- **User prompt on model changes**
  - When changes are detected, a `ContentDialog` is shown in the WinUI app describing:
    - Newly added models.
    - Removed models.
  - The user can choose to **re-import** the profile, which calls the existing `ImportProfile` logic and updates the active profile.

### 🛠️ Changed
- **VisualStudioSolutionService**
  - `GetSolutionFilePath(ProfileModel)` and `GetSolutionDirectory(ProfileModel)` now:
    - Use `ProfileModel.SolutionFilePath` if set.
    - Otherwise use the `ModelRootFolder` of the environment where `IsMainFOModel == true`.
    - Fall back to the legacy `_defaultSourceDirectory\ProfileName\ProfileName.sln` when no main model is available.
  - Legacy `GetSolutionFilePath(string)` and `GetSolutionDirectory(string)` are still supported for backward compatibility but should be phased out in favor of the profile-aware overloads.

- **AddProjectToSolution(ProfileModel, ProfileEnvironmentModel)**
  - Duplicate detection now uses the **model name marker** (e.g. `PtsTools`) to avoid adding the same model multiple times, regardless of path.
  - This ensures a model name appears **only once** in the `.sln`, even if the project display name contains additional text such as `PtsTools (ISV) [PtsTools]`.

- **Profile import behavior**
  - `ImportProfile` now respects existing solution files for the main FO model:
    - If a solution already exists in the main FO model’s repo, it is reused instead of creating a new one.
    - New source models are added to the current solution, avoiding duplicates by model name (using the `[ModelName]` convention).

### 🧰 Internal / Technical
- Introduced a `ModelSyncResult` type in the service layer to report added/removed models without any UI dependencies.
- Added an async `CheckProfileModelChangesAsync(ProfileModel)` in the profile service:
  - Runs in the service project with **no WinUI references**.
  - Called from the WinUI layer to decide when to present the “re-import profile” dialog.
- Profile sync / dialog logic is now hosted in the WinUI project (not in the shared service project), keeping a clean separation between UI and services.


## [0.9.1] – 2025-10-31

### ✨ Added
- **Compiled Model Support**
  - Profiles now support *compiled* (non-source) models found under `Libs/<ModelName>` containing `<ModelName>.xref`.
  - New `ModelType` enum added to `ProfileEnvironmentModel` (`Source`, `Compiled`).
  - Compiled models can be added directly or detected automatically when adding a new environment.
  - Compiled models appear in **blue** in the main list for clear visual distinction.

- **Expanded AddEnvironment Logic**
  - Automatically detects and registers:
    - Source models under `Metadata/<ModelName>`
    - Compiled models under `Libs/<ModelName>`
  - Both types can coexist inside the same repository.

- **Profile Clone Enhancements**
  - Added deep-clone support for `ProfileModel` and `ProfileEnvironmentModel`.
  - New helper: `SerializedClone<T>()` with selective cleanup (resets `PeriTask`, `PeriTaskComment`, `IsDeployed`).
  - Ensures cloned profiles do not share references with originals.

- **Repository Expand/Collapse in UI**
  - Double-click a repository header to expand or collapse its list of models.
  - Backed by `IsExpanded` in `RepoGroupViewModel`.
  - Uses `x:Load` for virtualization-safe loading/unloading.
  - Chevron indicator updates automatically based on expansion state.

- **Delete Profile Feature**
  - New **Delete Profile** button in the top menu.
  - Deletes both:
    - The profile from memory
    - The corresponding profile file on disk (after confirmation)
  - Includes full `MessageLogger` integration for auditing.

- **Improved Startup Behavior**
  - Repositories can now start expanded or collapsed reliably.
  - All expansion state updates run through UI-thread property setters.

### 🧩 Changed
- `ProfileService.ImportProfile` now returns a **ProfileModel** directly.
  - Updated all related UI paths to use the new return type.
  - Updated `BusyOps.TrySyncAsAsync<T>` and `TryAsync<T>` to support generic results.
- `AddEnvironment` refactored to unify compiled + source detection.
- All direct `Console.WriteLine` calls replaced with `MessageLogger`.

### 🧹 Fixed
- Resolved `SetConverterLookupRoot` crash caused by `x:Bind` inside WinUI templates.
- Implemented proper `INotifyPropertyChanged` for all expand/collapse logic.
- Eliminated flaky converter-based visibility by switching to reliable `x:Load`.
- Fixed issue where imported profiles returned `null` due to non-generic BusyOps routing.
- Ensured cloned profiles have fully independent environment lists.


---
## [0.9.0] – 2025-10-14

#### ✨ Added
- **Repository Grouping in Main Window**
  - Models belonging to the same Git repository are grouped under a single repo header.
  - Shared repo actions (**Git**, **Task**, **Open**, **Folder**, **Solution**) appear once per repo; per-model **Deploy/Undeploy/Remove** remain on each row.
  - Repo header shows decoded repo name and current branch.

- **Unified List Layout**
  - One seamless list (Git & non-Git together) using a template selector—no section headers.
  - Only **Remove** is right-aligned; all other buttons are left, next to the name/status.

- **PeriTask (Repo)**
  - New repo-level “Task” action assigns a **single PeriTask ID + comment** to **all models in that repo**, reusing the existing per-model `AssignPeriTask(...)`.

- **Busy Dialog / Busy Overlay Integration**
  - Centralized `TryCatch`/`TryCatchAsync` usage for long operations with **minimum visible time** to prevent flicker.
  - Start/complete/failure are logged via `MessageLogger` (no `Console.WriteLine`), with operation IDs scoped per run.

- **Settings Page**
  - Added new settings page

- **Edit Database Inline**
  - **Double-tap** the database field to enable inline editing; **Enter** saves, **Esc** cancels.
  - Persisted to the active profile; status reflects immediately.



## [0.8.6] - 2025-07-03

### Added
- ✨ Support for multi-model repositories in `AddEnvironment`:
  - When adding a repository containing multiple models under `metadata/`, each valid model is now automatically added to the profile.
  - Matching `.rnrproj` files under `project/<model>/<model>.rnrproj` are detected per model.  


### [0.8.5] – 2025-05-13

#### ✨ Added
- **Create Model from Scratch**  
  Added a `+ Create` button in the UI to generate a new model structure directly from the app.
  - Prompts for model name
  - Automatically generates folders, `.rnrproj`, and metadata
- **New `CreateModelFromScratch` service function** for profile-based model scaffolding
- **New UI integration**: "Create" button next to "Browse" and "Add Model"


## [0.8.4] – 2025-05-22

### Added
- **Convert Installed Model to Project Model**:
  - Installed models in `PackagesLocalDirectory` can now be converted to standard project format under `DefaultSourceDirectory`.
  - Automatically creates `Metadata/` and `Project/` folders with `.rnrproj` generated from template.
  - Registers the model directly into the provided profile (`ProfileModel.Environments`) with full metadata.

- **Auto-Detection in AddEnvironment**:
  - Calling `AddEnvironment()` with a path inside `_deploymentBasePath` triggers automatic model conversion.
  - After conversion, the model is not re-added but only registered into the solution and Git-checked.

- **Automatic Cleanup**:
  - After a successful conversion, the original installed model folder is deleted from `_deploymentBasePath`.
  - This includes automatic stop of `W3SVC` using the shared `ServiceHelper.StopW3SVC()` to release file locks.

### Changed
- Refactored `AddEnvironment` to clearly separate handling of installed vs. source models.
- Extracted reusable helpers like `IsInstalledModel`, `DetectModelNameFromMetadata`, and `RegisterConvertedModelToSolution` for clarity and DRY code.

---

## [0.8.3] – 2025-05-19

### Added
- **PeriTask Comment Support**:
  - Assign a comment alongside a PeriTask ID when assigning a task to a model.
  - Git branch format: `feature/task-1234-comment`
  - Automatically slugifies and truncates to fit Git branch name limits (255 characters).
- **WinUI UI Enhancement**:
  - Assign PeriTask dialog now includes a comment input box with live support for ID + comment.
- **Structured Git-safe Slugify helper** for branch creation.
- **Refactored service interaction pattern**:
  - All external service calls are now wrapped in a `TryCatch(...)` method.

### Changed
- Git branch switching now logs detailed results and stores PeriTask metadata even on failure.

### Fixed
- `RunProcess` no longer treats Git success messages (e.g., `"Switched to branch"`) as errors.
