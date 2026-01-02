# Geplante Features (als Codex-Prompts formuliert)

> Jede Aufgabe ist so formuliert, dass sie direkt als Prompt für Codex genutzt werden kann.

## 1) Duplikat-Scanner (Foto/Video) mit Review-UI
**Prompt:**
Implementiere einen Duplikat-Scanner auf Basis von perceptual hash (pHash/aHash) für Fotos und einem kombinierten Hash (Dateigröße + Dauer + Frame-Hash) für Videos. Füge eine neue Tabelle `MediaDuplicateGroup` (GroupId, MediaPath, Score, CreatedUtc) hinzu und baue in `Services/IndexService` eine Option, Duplikate nach dem Indexlauf zu gruppieren. Erstelle eine neue Seite `Views/DuplicatesPage.xaml` mit einem Review-Workflow (bestes Item behalten, übrige löschen/verschieben). Verwende `ThumbnailService` für Vorschau und respektiere `AllowFileChanges`. Stelle sicher, dass die UI nur Windows-spezifische Dateioperationen anbietet.

## 2) Hintergrund-Indexing mit Change-Tracking (Windows + Android)
**Prompt:**
Implementiere ein Change-Tracking für Medienquellen: Windows nutzt `FileSystemWatcher`, Android nutzt `ContentObserver` (MediaStore). In `Services/SourceService`/`IndexService` soll ein „Incremental Update“-Pfad entstehen, der neue/gelöschte Dateien in die DB schreibt, ohne den kompletten Index neu zu erstellen. Aktualisiere Settings um einen Schalter „Live-Indexing“ und füge Statusanzeige im `MainViewModel` hinzu.

## 3) Erweiterte Suche & Filter (People/Album/Location/Duration)
**Prompt:**
Erweitere die Suche so, dass sie neben `MediaItem.Name` auch People-Tags (`PersonTag`), Albumzugehörigkeiten (`AlbumItem`) und Location-Radius (GPS) unterstützt. Implementiere SQLite FTS5 für Textsuche und ein neues Filter-Panel in `Views/Components/MainSearchSortView`. Passe `IndexService.BuildQuery` und `AlbumService.BuildAlbumQuery` an und ergänze Unit-Tests in `UltimateVideoBrowser.Tests`.

## 4) Smart Collections / Auto-Alben
**Prompt:**
Füge „Smart Collections“ hinzu, die automatisch Alben auf Basis von Regeln generieren (z. B. „Letzte 30 Tage“, „Videos > 5 Minuten“, „Mit Person X“, „Im Radius von 10 km“). Erstelle neue Modelle `SmartAlbumRule`/`SmartAlbumDefinition`, eine `SmartAlbumService` sowie UI-Listen in `AlbumsPage.xaml`. Die Regeln sollen editierbar sein und in der DB gespeichert werden.

## 5) Backup/Restore für Datenbank & Einstellungen
**Prompt:**
Implementiere eine Backup/Restore-Funktion in den Settings: exportiere `ultimatevideobrowser.db` + `Preferences` in eine ZIP-Datei und stelle sie wieder her. Erstelle `Services/BackupService` und füge Aktionen in `SettingsPage.xaml` hinzu. Achte auf Plattform-spezifische Picker (Windows `FileSavePicker`, Android Share Sheet) und sichere Wiederherstellung mit Bestätigung/Restart-Hinweis.

## 6) Offline-Kartenmodus + Tile-Cache
**Prompt:**
Erweitere die Kartenansicht (`MapViewModel`/`MapPage.xaml`), sodass Tiles optional lokal gecached werden. Ergänze einen `MapTileCacheService`, der Tiles unter `FileSystem.CacheDirectory` speichert und eine maximale Cache-Größe erzwingt. Füge in Settings eine Option „Offline-Kartenmodus“ hinzu und nutze diese in `MapViewModel.BuildMapHtml`, um auf den lokalen Tile-Endpunkt zu zeigen.

