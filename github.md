# Ultimate Video Browser (Android 9+)

Ultimate Video Browser ist eine produktionsnahe .NET MAUI App für Android, die lokale Videos schnell indiziert, echte Vorschau-Frames generiert und eine performante Grid-Ansicht mit Suche und Sortierung bereitstellt.

## Highlights
- **MediaStore-Scan**: Liest alle lokalen Videos über den Android MediaStore.
- **SQLite-Index**: Speichert Metadaten inkrementell für schnelle Abfragen.
- **Echte Thumbnails**: Extrahiert Vorschaubilder über `MediaMetadataRetriever` und cached sie auf der Platte.
- **Schnelle Grid-Ansicht**: Adaptives Layout für Phone/Tablet/TV-ähnliche Ansichten.
- **Suche & Sortierung**: Suche nach Titeln sowie Sortierung nach Name, Datum oder Dauer.
- **System-Playback**: Öffnet Videos im Standard-Player („Open with“ kompatibel).

## Voraussetzungen
- **.NET 8 SDK**
- **MAUI-Workload**
- **Android SDK** (für Build/Deploy auf Gerät oder Emulator)

## Setup
1. Repository klonen.
2. MAUI-Workload installieren (falls noch nicht vorhanden):
   ```bash
   dotnet workload install maui
   ```
3. Android SDK konfigurieren (Android Studio oder CLI-Tools).

## Build
```bash
dotnet build -c Release
```

## Run (Android)
Start direkt aus der IDE (Visual Studio / Rider) oder via CLI:
```bash
dotnet build -t:Run -f net8.0-android
```

## App-Flow (kurz)
1. **Initiale Indizierung**: MediaStore wird gescannt und Metadaten in SQLite gespeichert.
2. **UI-Grid**: Videos werden aus dem Index geladen und angezeigt.
3. **Thumbnail-Pipeline**: Für die ersten sichtbaren Einträge werden echte Thumbnails erzeugt und zwischengespeichert.
4. **Playback**: Ein Tap öffnet den System-Player für das ausgewählte Video.

## Quellen (Sources)
- Standardmäßig existiert eine virtuelle Quelle „Alle Gerätvideos“ (MediaStore).
- Quellen lassen sich aktivieren/deaktivieren (UI: Sources Page).

## Suche & Sortierung
- **Suche**: Filtert den Index nach dem eingegebenen Text.
- **Sortierung**: Name, Datum oder Dauer.

## Datenspeicherung
- **SQLite-Datenbank**: Hält Video-Metadaten, um wiederholte Scans zu beschleunigen.
- **Thumbnail-Cache**: Lokale Dateipfade für Vorschau-Frames.

## Hinweise zu Netzlaufwerken (SMB/Windows Shares)
Android-Apps sollten **lokal synchronisierte Ordner** indizieren (z. B. über FolderSync/Solid Explorer). Direkter SMB-Scan ist nicht enthalten (Play-Store-friendly Ansatz).

## Projektstruktur (Überblick)
- `Views/` – XAML UI (MainPage, SourcesPage)
- `ViewModels/` – MVVM-Logik (Suche, Sortierung, Indexing)
- `Services/` – MediaStore-Scan, Index-Service, Thumbnail-Generierung, Playback
- `Models/` – Datenmodelle (VideoItem, MediaSource)
- `Resources/` – Strings, Themes, Styles

## Entwicklungshinweise
- Die Thumbnails werden asynchron erzeugt; die UI aktualisiert sich nach und nach.
- Änderungen an den Spalten/Layouts in `Views/MainPage.xaml` wirken sich direkt auf die Grid-Darstellung aus.

## Lizenz
Keine Lizenzdatei hinterlegt. Bitte bei Bedarf ergänzen.
