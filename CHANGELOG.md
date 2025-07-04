# FO Dev Manager Changelog

---

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
