# Ultimate Video Browser (Android 9+)

A production-style .NET MAUI Android app that:
- Scans all videos via Android MediaStore
- Indexes metadata in SQLite (incremental)
- Generates real thumbnails (frame extraction via MediaMetadataRetriever) with disk cache
- Offers fast grid browsing + search overlay
- Supports phone/tablet/TV-friendly UI (adaptive grid + focus visuals)
- Plays videos via the system default player ("Open with" compatible)

## Build
- Requires .NET 8 SDK + MAUI workload
- Android SDK installed

```bash
dotnet build -c Release
```

## Notes
- SMB/Windows shares: Android apps should index **local synced folders** (e.g., FolderSync/Solid Explorer sync).
  Direct SMB scanning is not included (Play-Store-friendly approach).
