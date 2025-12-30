# Third‑Party Notices (UltimateVideoBrowser)

This document lists **direct** third‑party components referenced by this repository (NuGet)
and the runtime-downloaded face recognition models used by the app.

> NOTE: Licenses can change between versions. Always verify against the exact version you ship.

---

## NuGet dependencies (direct)

| Component | Version | License | Notes |
|---|---:|---|---|
| CommunityToolkit.Maui | 13.0.0 | MIT | .NET MAUI Community Toolkit |
| CommunityToolkit.Maui.MediaElement | 7.0.0 | MIT | MediaElement wrapper |
| CommunityToolkit.Mvvm | 8.4.0 | MIT | MVVM Toolkit |
| Microsoft.ML.OnnxRuntime | 1.23.2 | MIT | ONNX Runtime inference engine |
| Microsoft.Maui.Controls | 10.0.20 | MIT (source); binaries may be under Microsoft licenses | Part of .NET MAUI |
| Microsoft.Maui.Essentials | 10.0.20 | MIT (source); binaries may be under Microsoft licenses | Part of .NET MAUI |
| Microsoft.Extensions.Logging.Debug | 10.0.1 | MIT | Debug logger provider |
| sqlite-net-pcl | 1.9.172 | MIT | SQLite wrapper for .NET |
| Xamarin.AndroidX.DocumentFile | 1.1.0.1 | MIT (binding package) | AndroidX binding package |
| SixLabors.ImageSharp | 3.1.12 | Six Labors Split License 1.0 | Apache 2.0 or Commercial depending on criteria |

---

## Runtime-downloaded ML models (OpenCV Zoo)

The app downloads these ONNX models at runtime when face recognition is enabled:

| Model | File | License | Origin |
|---|---|---|---|
| YuNet face detector | `face_detection_yunet_2023mar.onnx` | MIT (as published with the model) | OpenCV Zoo |
| SFace face recognition | `face_recognition_sface_2021dec.onnx` | Apache 2.0 (as published with the model) | OpenCV Zoo |

---

## Included license texts

This repo includes common license texts under `LICENSES/`:

- `MIT.txt`
- `Apache-2.0.txt`
- `SIXLABORS_SPLIT_LICENSE_1.0.txt`

If you ship binaries, include this notice file (or an equivalent “About / Licenses” screen) alongside your distribution.
