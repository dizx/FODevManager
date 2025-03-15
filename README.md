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
- **Visual Studio 2022** (for `.sln` file support)

### Build & Run

Clone or download the repository, then build and run:

```sh
# Build the application
 dotnet build

# Run the application
 dotnet run --project FODevManager
```

To create an **executable (**``**)**:

```sh
 dotnet publish -c Release -o publish
```

This will generate the executable in the `publish/` folder.

---

## Usage

### 1. Creating a Developer Profile

```sh
fodev.exe -profile "MyProfile" create
```

- Creates a **profile JSON file** in the application data folder.
- Creates a **Visual Studio Solution (**``**)** in the default source directory.

### 2. Listing Installed Profiles

```sh
fodev.exe list
```

- Displays all saved profiles.

### 3. Adding a Model to a Profile

```sh
fodev.exe -profile "MyProfile" -model "MyModel" add
```

- Adds the model to the **profile JSON**.
- Automatically **adds the model to the Visual Studio solution (**``**)**.
- If no path is provided, assumes the default **source directory** (configured in `appsettings.json`).

### 4. Listing Models in a Profile

```sh
fodev.exe list -models "MyProfile"
```

- Shows all models in the selected profile.

### 5. Checking Deployment Status of a Model

```sh
fodev.exe -profile "MyProfile" -model "MyModel" check
```

- Checks if the model’s **hard link is active** and if the project file exists.

### 6. Deploying a Model

```sh
fodev.exe -profile "MyProfile" -model "MyModel" deploy
```

- Creates a **hard link** from the **Metadata** folder to the **application directory**.

### 7. Removing a Model from a Profile

```sh
fodev.exe -profile "MyProfile" -model "MyModel" remove
```

- Removes the model from the profile JSON.
- **Removes the model from the Visual Studio solution (**``**)**.

### 8. Deleting a Profile

```sh
fodev.exe -profile "MyProfile" delete
```

- Deletes the profile JSON.
- **Deletes the associated Visual Studio solution (**``**)**.
- **Removes all models from the solution** before deletion.

---

## Configuration (`appsettings.json`)

The `appsettings.json` file allows customization of:

```json
{
  "ProfileStoragePath": "%APPDATA%/FODevManager",
  "DeploymentBasePath": "C:/AOSService/PackagesLocalDirectory",
  "DefaultSourceDirectory": "C:/Source/D365FO"
}
```

- **ProfileStoragePath**: Location where profiles (`.json` files) are stored.
- **DeploymentBasePath**: Where models are deployed.
- **DefaultSourceDirectory**: The default path where models and `.sln` files are created.

---

## Future Enhancements

- **GUI Version**
- **Bulk Model Deployment**
- **Dependency Management for Models**

---

## License

FODevManager is open-source and licensed under the **MIT License**.

---

## Support & Contributions

For issues or feature requests, open an **issue** or submit a **pull request** on the repository!

🚀 Happy Coding!

