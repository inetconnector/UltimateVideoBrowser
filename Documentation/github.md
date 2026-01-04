# UltimateVideoBrowser

UltimateVideoBrowser is a **.NET MAUI** media browser that builds a local **SQLite index** for fast browsing, filtering, and search.

**Implemented targets in this repository:**
- ✅ Android (`net10.0-android`)
- ✅ Windows (`net10.0-windows10.0.19041.0`)

---

## Why

If you have large photo/video collections, walking folders becomes slow and repetitive. This app builds a local index and a thumbnail cache so the UI can stay responsive while still working fully offline.

---

## Highlights

- Local **indexing** into SQLite (fast lists + search)
- **Photos / Videos** + optional **Documents**
- Multiple sources: platform libraries and custom folders
- **Thumbnail cache** for fast grids
- Filters: media type + date range, plus sorting/search
- Optional **People tagging** (on-device face detection + embeddings)

---

## Quick start

### Prerequisites

- **.NET 10 SDK**
- **.NET MAUI** workload

```bash
dotnet workload install maui
```

### Run (Windows)

```bash
dotnet restore
dotnet run -f net10.0-windows10.0.19041.0
```

### Run (Android)

```bash
dotnet restore
dotnet run -f net10.0-android
```

---

## Face recognition (optional)

People tagging is designed to run **on device** using **ONNX Runtime**. When enabled, the app can download two ONNX models (YuNet + SFace). For offline deployments you can pre-seed the models into the app’s local data.

License notices for the models and dependencies are documented in `THIRD_PARTY_NOTICES.md`.

---

## Docs

- Full documentation: see `README.md`
- License notices: `THIRD_PARTY_NOTICES.md` and `LICENSES/`

---

## Licensing note

- This repo includes **third‑party dependencies** and (optionally) downloaded ONNX models. Their licenses are documented in
  `THIRD_PARTY_NOTICES.md` and the license texts in `LICENSES/`.
- For commercialization / dual licensing guidance, see `LICENSING.md`.
- If you contribute, you agree to `CONTRIBUTOR_LICENSE_AGREEMENT.md`.

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
