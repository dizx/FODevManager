# FODevManager – Dynamics 365 FO Developer Profile Manager

## 🚀 Overview

**FODevManager** is a development utility for managing **Dynamics 365 Finance and Operations** (D365FO) model deployments via customizable **developer profiles**.

The system includes:
- A powerful **.NET 9 console CLI**
- A modern **WinUI 3 desktop UI**
- Git-aware model tracking
- Automated profile switching, PeriTask integration, and structured logging

## ✅ Key Features

### 🧩 Profile Management
- Create, delete, and switch between multiple isolated developer profiles
- Each profile has a custom Visual Studio `.sln` and database config
- Easily import/export profile JSON files

### 📦 Model Management
- Add and remove models from profiles using CLI or UI
- View deployment status, Git repo info, and PeriTask assignment

### 🔗 Deployment Automation
- Deploy/undeploy models via hard links or junctions to `PackagesLocalDirectory`
- Automatic update of profile JSON deployment status
- Git clean-check before switching branches

### 💼 Git Integration
- Detect if a model is in a Git repository
- Switch to a PeriTask-based branch (`feature/TASK-1234`)
- Open remote repo URLs in the browser

### 🖥 WinUI 3 GUI (New!)
- Modern interface for managing models, profiles, and deployment
- Inline Git and PeriTask actions
- Multi-line status logger with message severity
- Profile import/export, build info, and icon-based buttons

## 🛠 Installation

### ✅ Prerequisites
- **.NET 9 SDK** ([Download](https://dotnet.microsoft.com/en-us/download/dotnet/9.0))
- **Windows 11 or 10** (Dev Mode enabled)
- **Visual Studio 2022**
- (Optional) **Git CLI**, **PeriTask access**

### 🧱 Build and Run

```bash
dotnet build
dotnet publish -c Release -o publish
```

Then run either:
- `fodev.exe` (console CLI)
- `FODevManager.WinUI.exe` (desktop app)

## 📂 Project Structure

```
/FODevManager
 ├── Program.cs                      # Console entry point
 ├── WinUI/                          # GUI project
 ├── Services/
 │    ├── ProfileService.cs
 │    ├── ModelDeploymentService.cs
 │    └── VisualStudioSolutionService.cs
 ├── Models/
 ├── Utils/
 ├── Config/
 ├── appsettings.json
 ├── Tests/
 ├── publish/
```

## 🧪 CLI Usage

```sh
fodev.exe -profile "DevProfile" <command> [options]
```

### 🔧 Profile Commands
| Command | Description |
|--------|-------------|
| `create` | Creates a new profile |
| `delete` | Deletes a profile |
| `check`  | Validates paths/configs |
| `list`   | Lists available profiles |

### 🧱 Model Commands
| Command | Description |
|--------|-------------|
| `-model "Name" add "path"` | Adds a model to the profile |
| `-model "Name" remove`     | Removes the model |
| `-model "Name" check`      | Validates model deployment |
| `list`                     | Lists all models in the profile |

### 🚀 Deployment Commands
| Command | Description |
|--------|-------------|
| `deploy`             | Deploys all undeployed models |
| `-model "Name" deploy` | Deploys a specific model |

### 🔀 Git + PeriTask
| Command | Description |
|--------|-------------|
| `git-check`         | Checks if model path is a Git repo |
| `git-open`          | Opens remote repo in browser |
| `switch-task 1234`  | Creates & switches to branch `feature/TASK-1234` |

## ⚙ Configuration

Edit `appsettings.json`:

```json
{
  "ProfileStoragePath": "%APPDATA%/FODevManager",
  "DeploymentBasePath": "C:/AOSService/PackagesLocalDirectory",
  "DefaultSourceDirectory": "C:/Source/D365FO"
}
```

## 🪟 GUI Features (WinUI)

| Feature         | Details |
|----------------|---------|
| Profile dropdown | Auto-load profiles from config |
| Model table     | Git-aware, deploy status, task ID |
| PeriTask assign | Supports branch creation & URL opening |
| Git repo link   | Button opens remote repo |
| Deployment      | Model-level and profile-level deploy/undeploy |
| Logging         | Multi-line status pane with message severity |
| About dialog    | Shows version + build date |
| Import profile  | Load profile JSON via folder picker |

## 🧪 Running Unit Tests

```bash
dotnet test
```

Covers `CommandParser`, `ProfileService`, and `ModelDeploymentService`.

## 🧭 Roadmap

- ✅ WinUI 3 frontend (v1 complete)
- ✅ Git branch switch integration
- 🧪 Logging via Serilog (in progress)
- 🧱 MSIX packaging or installer (future)
- 🧊 Multi-user profile sharing (future)
- 🧪 Test coverage for more services

## 🤝 Contributing

- Open issues or PRs on GitHub
- Contact **mortenaa@gmail.com** for questions or feature requests

## 📜 License

Licensed under the **MIT License**
