# FO Dev Manager Changelog


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
