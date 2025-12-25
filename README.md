# Ultimate Video Browser (Android 9+)

Ultimate Video Browser ist eine produktionsnahe .NET MAUI App für Android, die lokale Videos schnell indiziert, echte Vorschaubilder erzeugt und eine performante Grid-Ansicht mit Suche und Sortierung bietet.

## Highlights
- **MediaStore-Scan**: Liest lokale Videos über den Android MediaStore.
- **SQLite-Index**: Inkrementelle Speicherung der Metadaten für schnelle Abfragen.
- **Echte Thumbnails**: Frame-Extraktion via `MediaMetadataRetriever` mit lokalem Disk-Cache.
- **Schnelles Grid-Browsing**: Adaptives Layout für Phone/Tablet/TV-ähnliche Oberflächen.
- **Suche & Sortierung**: Filter nach Titel sowie Sortierung nach Name, Datum oder Dauer.
- **System-Playback**: Öffnet Videos im Standard-Player („Open with“ kompatibel).

## Voraussetzungen
- **.NET 8 SDK**
- **MAUI-Workload**
- **Android SDK**

## Build
```bash
dotnet build -c Release
```

## Run (Android)
```bash
dotnet build -t:Run -f net8.0-android
```

## Hinweise zu Netzlaufwerken (SMB/Windows Shares)
Android-Apps sollten **lokal synchronisierte Ordner** indizieren (z. B. FolderSync/Solid Explorer). Direkter SMB-Scan ist nicht enthalten (Play-Store-friendly Ansatz).

## Detaillierte Doku
Eine ausführliche Dokumentation befindet sich in `github.md`.
