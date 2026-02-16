# FODev Manager – Context 

FODev Manager is a WinUI tool for managing D365FO developer “profiles”. A profile is a working set of repositories and models, plus optional environment settings (notably database name).

## Key concepts
- **ProfileModel**: ProfileName, DatabaseName, SolutionFilePath, ProfileFilePath, Repositories + standalone Models.
- **RepositoryModel**: RepoRootFolder/GitUrl, PreferredBranch/MainBranchName, automation flags (auto-checkout, auto-stash), Task/TaskComment, Models in the repo.
- **ProfileEnvironmentModel**: A model entry (source or compiled) with paths and deployment flags.

## Main capabilities (current)
- Create/switch/delete profiles; maintain a VS solution per profile.
- Import/export profiles (local JSON and repo-based bootstrap via `Artifacts` folder).
- Add/create models; detect source vs compiled models.
- Deploy/undeploy models via symlink flow (service stop/start as needed).
- Git integration per repo: branch detection, dirty/conflict checks, fetch/pull, merge main into current, optional auto-checkout on profile load.
- Background monitoring: git health indicators + external profile JSON change detection and re-import prompt.
- Apply database name to environment config (stop/start service around changes).

## EasyGit (WinUI companion)
- `EasyGit.WinUI` is a focused git workflow UI for repositories inside a selected profile.
- Workflow is stage-based per repository: `create -> commit -> create pr -> complete`.
- Stage and PR metadata are persisted on `RepositoryModel` (`WorkflowStage`, `FeatureBranchName`, `PullRequestUrl`, `PullRequestId`).
- Button behavior is gated by stage:
  - `Create Task/Feature` is disabled once workflow starts.
  - `Create PR` is shown only when no PR exists.
  - `View PR` is shown only when a PR exists.
  - `Complete` is available after PR creation.
- `Complete` flow: verify PR merge status when possible (Azure DevOps PAT + PR ID), switch to main, pull, then delete feature branch (local/remote best effort), and clear workflow metadata.
- If merge cannot be verified, completion is still allowed after explicit user confirmation.
- Top toolbar includes `Git Actions -> Git Reset` to reset all repositories in the selected profile back to main and reset workflow metadata.
- Profile switching includes branch memory sync: when moving between profiles that share the same repo, EasyGit auto-checks out the remembered branch.
- Busy/loading overlay is used for all user-triggered git workflow operations.

## Repo structure conventions
- Repo root typically contains:
  - `Metadata/{ModelName}/...` (X++ source)
  - `Project/{ModelName}/{ModelName}.rnrproj` (VS project)
  - `Artifacts/` (exported profile JSON)

## Implementation notes
- UI groups models by repository (repo headers + non-git models).
- Long operations are asynchronous; UI updates are dispatched back to the UI thread.
- Git operations shell out to `git`; failures are logged.
