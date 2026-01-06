# UltimateVideoBrowser

UltimateVideoBrowser is a **.NET MAUI** app that indexes local media into a **SQLite** database for fast browsing, filtering, and search on your device.

**Targets implemented in this repository:**
- ✅ **Android** (`net10.0-android`)
- ✅ **Windows** (`net10.0-windows10.0.19041.0`)

> Other MAUI targets (iOS/macOS) are not implemented in this branch.

---

## Table of contents

- [Features](#features)
- [Platform support](#platform-support)
- [Quick start](#quick-start)
- [Configuration and settings](#configuration-and-settings)
- [How indexing works](#how-indexing-works)
- [People tagging and face recognition](#people-tagging-and-face-recognition)
- [Map view and location metadata](#map-view-and-location-metadata)
- [Storage locations](#storage-locations)
- [Privacy](#privacy)
- [Repository layout](#repository-layout)
- [Troubleshooting](#troubleshooting)
- [Roadmap](#roadmap)
- [Contributing](#contributing)
- [Licensing and third-party notices](#licensing-and-third-party-notices)

---

## Features

- **Local media indexing** into SQLite for fast listing and search
- Supports **Photos**, **Videos**, and optional **Documents**
- **Multiple sources**:
  - Platform default libraries (Pictures/Videos/Documents)
  - Custom folders
- **Timeline + grid browser** with filters (type/date range) and sorting
- **Albums** (create, rename, add/remove items)
- **People tagging** (optional): on-device face detection + embeddings
- **Map view** (optional) for media with GPS metadata
- **Thumbnail cache** for responsive grids/lists
- **Database backup/restore** (export/import ZIP including settings and thumbnails)
- **Internal preview** (video player + image/doc preview)
- **Share / Save As / Copy / Move / Delete** (platform-dependent)

---

## Platform support

| Feature | Android | Windows |
|---|---:|---:|
| Scan default media libraries | ✅ (MediaStore) | ✅ (Known folders) |
| Scan custom folder | ✅ (SAF/DocumentFile) | ✅ (folder picker + stored token) |
| Thumbnail caching | ✅ | ✅ |
| Internal playback (MediaElement) | ✅ | ✅ |
| People tagging (ONNX) | ✅ | ✅ |
| Map view (Leaflet + OSM tiles) | ✅ | ✅ |
| Copy/Move/Delete files (Allow file changes) | ⚠️ (not implemented) | ✅ |
| Save As / Export | ✅ (Share sheet) | ✅ (FileSavePicker) |

---

## Quick start

### Prerequisites

- **.NET 10 SDK** (this repo targets `net10.0-*`)
- **.NET MAUI workload**
- For Windows: Windows 10 SDK compatible with `10.0.19041.0`

Install MAUI workload:

```bash
dotnet workload install maui
```

### Build & run (Windows)

```bash
dotnet restore
dotnet build -f net10.0-windows10.0.19041.0
dotnet run   -f net10.0-windows10.0.19041.0
```

### Build & run (Android)

Use Visual Studio / VS Code MAUI tooling, or:

```bash
dotnet restore
dotnet build -f net10.0-android
dotnet run   -f net10.0-android
```

#### Android permissions

Indexing requires media read permission. The app requests it via `PermissionService` before scanning.

---

## Configuration and settings

User-facing settings are persisted via `Preferences` in `Services/AppSettingsService.cs`.

Common options you will see in the UI:

- **Theme** (dark by default)
- **Internal player** (MediaElement)
- **People tagging** (face detection + embeddings)
- **Location metadata + Map view** (reads GPS EXIF + shows map)
- **Indexed media types** vs **visible media types** (you can index more than you display)
- **Date filter** (from/to + enable toggle)
- **Search text** and **sort mode**
- **Custom extension lists** for photos/videos/documents
- **Allow file changes** (controls whether the app is allowed to copy/move/delete)

The Settings page also includes **Backup/Restore** (export/import ZIP containing the database, settings, and thumbnail cache).

Tip: If you change settings that affect indexing, the app may set a “needs reindex” flag.

---

## How indexing works

Indexing produces a local SQLite index that is used for UI lists, search, filters, and the thumbnail cache.

### Sources

A media source is represented by a `MediaSource`.

- If a source specifies a **local folder path**, indexing scans that folder.
- If no folder is specified, indexing falls back to **platform defaults**.

### Windows

- Default roots: **My Pictures**, **My Videos**, **My Documents**
- When you pick a folder, the app stores an access token for future runs.

### Android

- If no folder is configured, scanning uses **MediaStore** (fast, no deep recursion).
- If a folder is configured, scanning uses **Storage Access Framework (SAF)** via `DocumentFile`.

---

## People tagging and face recognition

People tagging is optional and designed for **on-device** processing.

High-level pipeline:

1. Decode and preprocess images (ImageSharp)
2. Run face detection model (ONNX Runtime)
3. For each face: compute embedding (ONNX Runtime)
4. Store embeddings locally and match by distance threshold

The People UI supports:
- **Auto-generated people** from face recognition
- **Manual name edits** and **ignore** toggles
- **Manual tags** in the photo editor (stored in `PersonTag`)

### Free vs Pro

People tagging has a free trial and a free limit:

- **Free trial:** in non-Pro mode, people tagging is available for **14 days** and then auto-disables.
- **Free limit:** in non-Pro mode, the app can create up to **20** people profiles; beyond that, it will prompt to upgrade.

In **Pro** mode, people tagging stays enabled and the people limit is removed.

### Model downloads

When the feature is enabled, the app can download two ONNX models (YuNet + SFace) used for face detection and recognition.

The download logic is implemented in:

- `Services/Faces/ModelFileService.cs`

If you distribute this app as a binary, ensure you also distribute the required license notices for these models.
See [Licensing and third-party notices](#licensing-and-third-party-notices).

### Offline / pre-seeding models

If you need a fully offline setup, you can pre-seed the model files into the expected local storage directory.
The exact storage location is shown below.

---

## Map view and location metadata

- Location metadata is read from **GPS EXIF** (photos) and platform-specific metadata (videos).
- The **Map view** renders a Leaflet map and uses **OpenStreetMap tiles**.
- Map tiles require network access unless you provide an offline tile cache.

---

## Storage locations

Paths are per-user and platform-specific. The app uses MAUI `FileSystem.*` locations.

- **Database:** `FileSystem.AppDataDirectory/ultimatevideobrowser.db`
- **Thumbnail cache:** `FileSystem.CacheDirectory/thumbs/`
- **Face models:** stored in app-local data (see `ModelFileService`)

---

## Privacy

- Indexing and browsing are local.
- Thumbnails and the database are stored locally.
- People tagging runs locally (no cloud inference).

Network access is only required if you enable face recognition (model download) or if you use the Map view (OSM tiles).

---

## Repository layout

- `Services/` – indexing, scanning, permissions, thumbnails, face recognition
- `Models/` – SQLite models / DTOs
- `ViewModels/` – MVVM view models
- `Views/` – MAUI pages + components
- `Platforms/Android`, `Platforms/Windows` – platform-specific services/helpers
- `UltimateVideoBrowser.Tests/` – unit tests (logic-level)

---

## Troubleshooting

### Android: folder scan finds nothing

- If you selected a folder via SAF, make sure the app still has access to it (some devices revoke access after updates).
- Try re-selecting the folder and re-run indexing.
- Verify that the file extensions you want are included in **Settings → Extensions**.

### Android: permission prompt keeps returning “denied”

- Ensure you granted the media read permissions in system settings.
- On some Android versions, photo/video permissions are separate.

### Face recognition: models cannot be downloaded

- Model downloads require network access at least once.
- For offline deployments, pre-seed the model files into the app’s local data directory and restart the app.
- If you ship binaries, ship the license notices for the models (see `THIRD_PARTY_NOTICES.md`).

### Windows: access denied when scanning a folder

- Re-pick the folder so the access token is refreshed.
- Avoid scanning protected system locations.

### Map view shows no pins

- Ensure **Locations** are enabled in Settings and re-run indexing.
- Only media with GPS metadata will appear on the map.

### Build issues

- Confirm you have the **.NET 10 SDK** installed.
- Confirm MAUI workloads are installed (`dotnet workload install maui`).
- On Windows, ensure the target `10.0.19041.0` Windows SDK is available.

---

## Roadmap

See `PLANNED_FEATURES.md` for a curated, prompt-ready list of upcoming features.

---

## Contributing

If you want to contribute:

- Keep changes small and focused.
- Prefer adding unit tests for logic-heavy changes (`UltimateVideoBrowser.Tests`).
- Avoid introducing new dependencies unless necessary (and update `THIRD_PARTY_NOTICES.md` accordingly).

---

## Licensing and third-party notices

### Repository license

This repository contains **Inetconnector-owned code** plus **third‑party components** (NuGet packages and optionally downloaded models).

- To pick an outbound licensing strategy that supports commercialization (permissive, dual-license, or source-available),
  see: **`LICENSING.md`**.
- If you accept external pull requests and want to keep the ability to commercialize/dual‑license later, have contributors
  agree to: **`CONTRIBUTOR_LICENSE_AGREEMENT.md`**.

> Note: Third‑party components remain licensed under their own terms. See `THIRD_PARTY_NOTICES.md` and `LICENSES/`.


### Third-party dependencies

This app uses third-party components with license notice requirements.
This repository includes:

- `THIRD_PARTY_NOTICES.md` – direct NuGet dependencies + runtime-downloaded ML models
- `LICENSES/` – license texts referenced by the notices

If you distribute binaries, ship these notices alongside your distribution (or provide an in-app “About / Licenses” screen).

### Six Labors Split License (ImageSharp)

`SixLabors.ImageSharp` is under the **Six Labors Split License**.

- If you qualify (for example **< 1M USD annual gross revenue**, or using it as **transitive dependency**, or in **open source/source-available**, or as **non-profit**), you can use it under **Apache 2.0**.
- Otherwise, a paid commercial license may be required.

See `LICENSES/SIXLABORS_SPLIT_LICENSE_1.0.txt` and `THIRD_PARTY_NOTICES.md`.

## License

UltimateVideoBrowser is **dual-track**:

- **Community license:** Apache-2.0 (see `LICENSE`)
- **Commercial licensing:** available from Inetconnector.com for OEM/support/procurement needs
  (see `LICENSES/INETCONNECTOR_COMMERCIAL_LICENSE.txt` and `LICENSING.md`)

**Third-party components** (NuGet packages and downloaded ML models) remain under their respective licenses.
See `THIRD_PARTY_NOTICES.md` and `LICENSES/`.

**Contributions** require acceptance of the CLA (`CONTRIBUTOR_LICENSE_AGREEMENT.md`).

## Trademarks

The names and logos are trademarks of Inetconnector.com. See `TRADEMARKS.md` for permitted use.
