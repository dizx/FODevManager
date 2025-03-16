# FODevManager - Dynamics 365 FO Developer Profile Manager

## Overview

FODevManager is a .NET 9 console application designed to manage **developer profiles** for **Dynamics 365 FO (Finance and Operations)** development environments. It automates the creation of profiles, management of models, and deployment of metadata using **hard links**.

## Features

- **Manage Developer Profiles**: Create, list, and delete profiles.
- **Manage Models**: Add, remove, and check model deployment status.
- **Automatic Visual Studio Solution Management**: Each profile has a `.sln` file to manage projects.
- **Deployment Handling**: Creates hard links for metadata deployment.
- **Configuration Management**: Uses `appsettings.json` for directory structure customization.

## Installation

### Prerequisites

- **.NET 9 SDK** ([Download Here](https://dotnet.microsoft.com/en-us/download/dotnet/9.0))
- **Windows OS** (with Developer Mode enabled for hard links)
- **Visual Studio 2022**


## **📂 Project Structure**
```
/FODevManager
 ├── Program.cs
 ├── Utils
 │    ├── CommandParser.cs                 # Handles command-line arguments
 │    ├── FileHelper.cs                    # Handles file operations
 ├── Config
 │    ├── AppConfig.cs                      # Configuration class for settings
 ├── Models
 │    ├── ProfileModel.cs                   # Represents a developer profile
 │    ├── ProfileEnvironmentModel.cs        # Represents an environment inside a profile
 ├── Services
 │    ├── ProfileService.cs                 # Manages profile operations
 │    ├── ModelDeploymentService.cs         # Handles model deployment
 │    ├── VisualStudioSolutionService.cs    # Manages .sln files
 ├── appsettings.json                        # Configuration file
 ├── Tests
 │    ├── CommandParserTests.cs             # Unit tests for argument parsing
 ├── publish/                                # Compiled executable
```

---

## **📦 Installation**
### **1. Build the Application**
```sh
dotnet build
```

### **2. Publish for Deployment**
```sh
dotnet publish -c Release -o publish
```
This creates a **standalone executable** in the `publish/` folder.

---

## **⚙ Configuration (`appsettings.json`)**
Modify this file to set the paths for **profiles, deployments, and model sources**.

```json
{
  "ProfileStoragePath": "%APPDATA%/FODevManager",
  "DeploymentBasePath": "C:/AOSService/PackagesLocalDirectory",
  "DefaultSourceDirectory": "C:/Source/D365FO"
}
```
- **`ProfileStoragePath`** → Directory where profiles are stored.  
- **`DeploymentBasePath`** → Directory where models are deployed.  
- **`DefaultSourceDirectory`** → Default location where model sources are located.

---

## **🚀 Usage (CLI Commands)**
All commands follow this format:
```
fodev.exe -profile "ProfileName" <command> [options]
```

### **📌 Profile Management**
| **Command** | **Description** |
|------------|----------------|
| `fodev.exe -profile "MyProfile" create` | Creates a new profile |
| `fodev.exe -profile "MyProfile" delete` | Deletes a profile |
| `fodev.exe -profile "MyProfile" check` | Checks if all profile-related files exist |
| `fodev.exe list` | Lists all profiles |

---

### **📌 Model Management**
| **Command** | **Description** |
|------------|----------------|
| `fodev.exe -profile "MyProfile" -model "MyModel" add "C:\Path\to\project.rnrproj"` | Adds a model to a profile |
| `fodev.exe -profile "MyProfile" -model "MyModel" remove` | Removes a model from a profile |
| `fodev.exe -profile "MyProfile" -model "MyModel" check` | Checks if the model is deployed |

---

### **📌 Model Deployment**
| **Command** | **Description** |
|------------|----------------|
| `fodev.exe -profile "MyProfile" -model "MyModel" deploy` | Deploys a single model |
| `fodev.exe -profile "MyProfile" deploy-all` | Deploys all **undeployed** models in the profile |

---

## **🔍 How Deployment Works**
1. **When a model is added**, it is stored in the profile JSON file.
2. **During deployment**, the tool:
   - Creates a **hard link** between the `Metadata` folder and the `DeploymentBasePath`.
   - Updates the **profile JSON** to mark the model as deployed.
3. **`deploy-all`** finds **all undeployed models** and deploys them.

---

## **🧪 Running Unit Tests**
We use **NUnit** for testing.

### **📌 Run All Tests**
```sh
dotnet test
```

### **📌 Test Coverage**
✅ **CommandParserTests** (Ensures correct CLI parsing)  
✅ **(Upcoming) Tests for ProfileService & ModelDeploymentService**

---

## **📌 Upcoming Features**
- ✅ **Deploy all undeployed models** *(Added!)*
- 🏗 **Logging system** *(Log all operations to a file)*
- 🏗 **Bulk deletion of profiles**
- 🏗 **GUI version (future expansion)**

---

## **🤝 Contributions**
Feel free to **open issues** or **submit pull requests**.  
For discussions, please use the **GitHub Issues** tab.

---

## **📜 License**
This project is licensed under the **MIT License**.

---

### **💡 Need Help?**
📧 Contact **mortenaa@gmail.com** or create an **issue on GitHub**.