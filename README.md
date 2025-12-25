# Ultimate Video Browser

Ultimate Video Browser is a production-ready .NET MAUI application that indexes local videos, generates real preview frames, and delivers fast grid browsing with search and sorting. The app is optimized for large libraries and modern Android/Windows devices.

## Table of Contents
- [Features](#features)
- [Supported Platforms](#supported-platforms)
- [Tech Stack](#tech-stack)
- [Project Structure](#project-structure)
- [Getting Started](#getting-started)
  - [Prerequisites](#prerequisites)
  - [Setup](#setup)
- [Build & Run](#build--run)
  - [Android](#android)
  - [Windows](#windows)
- [App Flow](#app-flow)
- [Data & Storage](#data--storage)
- [Permissions](#permissions)
- [Networking & SMB Shares](#networking--smb-shares)
- [Troubleshooting](#troubleshooting)
- [Roadmap / Extending to Other Platforms](#roadmap--extending-to-other-platforms)
- [License](#license)

## Features
- **MediaStore scan (Android)**: Fast discovery of local videos through the Android MediaStore.
- **SQLite indexing**: Incremental metadata storage for fast queries and quick startup.
- **Real thumbnails**: Frame extraction via `MediaMetadataRetriever` with a disk cache.
- **High-performance grid UI**: Adaptive layout for phone, tablet, and TV-like form factors.
- **Search & sorting**: Filter by title and sort by name, date, or duration.
- **System playback**: Launches the platform’s default player via system intents.

## Supported Platforms
This repository currently targets the following .NET MAUI platforms:

| Platform | Target Framework | Minimum OS | Notes |
| --- | --- | --- | --- |
| **Android** | `net9.0-android` | Android 9 (API 28) | Full feature set (MediaStore + thumbnails) |
| **Windows** | `net9.0-windows10.0.19041.0` | Windows 10/11 | UI and local indexing support |

Other MAUI platforms (iOS, macOS, Mac Catalyst) are **not configured** in the project file yet. See [Roadmap / Extending to Other Platforms](#roadmap--extending-to-other-platforms) for guidance.

## Tech Stack
- **.NET 9** + **.NET MAUI**
- **SQLite** (`sqlite-net-pcl`)
- **MVVM Toolkit** (`CommunityToolkit.Mvvm`)
- **Android Media APIs** for video indexing and thumbnail extraction

## Project Structure
- `Views/` – XAML UI (e.g., `MainPage`, `SourcesPage`)
- `ViewModels/` – MVVM logic (search, sorting, indexing)
- `Services/` – MediaStore scan, index service, thumbnails, playback
- `Models/` – data models (`VideoItem`, `MediaSource`)
- `Resources/` – strings, themes, styles
- `Platforms/` – platform-specific implementations

## Getting Started
### Prerequisites
- **.NET 9 SDK**
- **MAUI workload** (`dotnet workload install maui`)
- **Android SDK** (Android Studio or CLI tools) for Android builds
- **Windows 10/11 SDK** for Windows builds

### Setup
1. Clone the repository.
2. Install workloads (if needed):
   ```bash
   dotnet workload install maui
   ```
3. Ensure Android and Windows SDKs are installed and configured.

## Build & Run
### Android
Build a release APK:
```bash
dotnet build -c Release -f net9.0-android
```

Run on a connected device or emulator:
```bash
dotnet build -t:Run -f net9.0-android
```

### Windows
Build a Windows desktop app:
```bash
dotnet build -c Release -f net9.0-windows10.0.19041.0
```

Run the Windows target:
```bash
dotnet build -t:Run -f net9.0-windows10.0.19041.0
```

> Note: The project is configured with `WindowsPackageType=None` for unpackaged desktop deployment.

## App Flow
1. **Initial indexing**: Media metadata is scanned and stored in SQLite.
2. **Grid rendering**: The UI loads results quickly from the index.
3. **Thumbnail pipeline**: Preview frames are generated asynchronously and cached on disk.
4. **Playback**: Tapping a video opens the system player.

## Data & Storage
- **SQLite database**: Caches metadata to avoid full rescans.
- **Thumbnail cache**: Local file cache for preview frames.
- **Incremental refresh**: New videos are indexed without rebuilding the full catalog.

## Permissions
Android requires media access permissions to read local videos. Ensure the runtime permission flow is enabled and accepted on device. Without permission, the app will not list local videos.

## Networking & SMB Shares
The app intentionally **does not scan SMB shares directly**. For best performance and platform compliance, sync network folders locally first (e.g., with FolderSync, Solid Explorer, or similar) and point the app to the local folder.

## Troubleshooting
- **No videos appear**: Verify storage permissions and confirm there are local videos on the device.
- **Thumbnails are blank**: Some codecs may not support frame extraction. Try another file.
- **Slow first launch**: Initial indexing can take time on large libraries; subsequent runs are fast.

## Roadmap / Extending to Other Platforms
To add additional MAUI targets (iOS/macOS/Mac Catalyst):
1. Add new target frameworks in `UltimateVideoBrowser.csproj` (e.g., `net9.0-ios`, `net9.0-maccatalyst`).
2. Implement platform-specific media scanners and thumbnail extraction in `Platforms/<Platform>`.
3. Validate permissions and sandbox behavior for each platform.

## License
No license file is included. Add one if you plan to distribute or open-source the project.
