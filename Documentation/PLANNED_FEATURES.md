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

## 7) UI/UX-Verbesserungen: Suche/Status/Location-Opt-In (ausführlich mit Eventualitäten)
**Prompt:**
Erweitere die UI/UX in drei Bereichen und decke alle relevanten Eventualitäten, Erweiterungen und Haftungsaspekte ab. Die Änderungen sollen für Windows und Android konsistent erscheinen, mit plattformspezifischen Anpassungen nur dort, wo zwingend nötig. Dokumentiere die UI-Texte zentral, damit sie lokalisiert werden können.

### A) Suche über Tags/Personen/Album (erweiterte Filter)
1. **Scope der Suche erweitern**
   - Ergänze die Suche so, dass sie neben `MediaItem.Name` auch `PersonTag.PersonName`, Albumtitel (`Album.Name`) und Albumzugehörigkeiten (`AlbumItem`) berücksichtigt.
   - Nutze bestehende Query-Pfade (`IndexService.BuildQuery`, `AlbumService.BuildAlbumQuery`) und ergänze einen kombinierten Filter-Builder, der die Teilfilter logisch verknüpft (Standard: OR über Quellen, optional AND für „alle Begriffe müssen matchen“).
2. **UI-Filter & Suchchips**
   - Implementiere Filter-Chips/Toggle-Buttons in `Views/Components/MainSearchSortView`, die Auswahl zwischen „Titel“, „Personen“, „Alben“ erlauben sowie eine Option „Alle“.
   - Füge eine klare visuelle Darstellung der aktiven Filter hinzu (z. B. Chips mit X zum Entfernen).
3. **Edge Cases**
   - Kein Ergebnis: zeige einen erklärenden Leerzustand mit Hinweisen, welche Filter aktiv sind.
   - Leerer Suchstring: erlaube das Zurücksetzen der Filter zu „Alle“ und nutze dann Standard-Sortierung.
   - Mehrdeutige Namen: wenn „Person“ gleich Albumname ist, markiere die Trefferart im UI.
4. **Erweiterungen**
   - Optionaler Switch „exakte Übereinstimmung“ (Exact Match) und „Teilwortsuche“.
   - Vorbereitung für FTS5 (falls später eingebaut), aber der UI-Flow bleibt gleich.

### B) Eindeutige Statusanzeigen für Indexing
1. **Status-Definitionen**
   - „Index läuft“: aktiver Scan/Import, Fortschritt sichtbar.
   - „Index steht“: zuletzt erfolgreich abgeschlossen, keine laufenden Tasks.
   - „Index benötigt“: Quellen geändert oder Index invalidiert (z. B. nach App-Update), Index nicht aktuell.
2. **Synchronisierte Darstellung**
   - Synchronisiere Banner-Status im Hauptscreen mit dem Status in den Settings. Beide Views nutzen dieselbe Statusquelle (`IndexService`/`MainViewModel`) und denselben Statusenum.
   - Zeige ein einheitliches Icon-Set + Farbcodes (z. B. Gelb = benötigt, Grün = abgeschlossen, Blau = läuft).
3. **Edge Cases**
   - App im Hintergrund: Status nach Resume aktualisieren.
   - Abgebrochener Indexlauf: Status explizit „unterbrochen“/„benötigt“ anzeigen, inkl. Aktion „Fortsetzen“.
   - Fehlerzustand: Fehlermeldung mit Log-Hinweis und CTA „Details anzeigen“ (falls Logs vorhanden).
4. **Erweiterungen**
   - Fortschrittsdetails (Quelle, Anzahl gefundener Dateien, geschätzte Restzeit).
   - Optionaler „Automatisch indizieren“-Schalter, der Status-Erklärtext dynamisch anpasst.

### C) Lokationsdaten-Opt-In (GPS-EXIF & Tile-Server)
1. **Opt-In-Dialog**
   - Vor Aktivierung der Karten-/Locations-Features einen klaren Opt-In-Dialog anzeigen.
   - Explizite Erklärung, dass GPS-Daten aus EXIF gelesen werden und die Kartenansicht externe Tile-Server anfragt.
2. **UI-Texte & Transparenz**
   - Klarer Hinweis in Settings (z. B. „GPS-EXIF aus Fotos/Videos wird verarbeitet“).
   - Hinweis auf mögliche Datenweitergabe an Tile-Server (IP/Anfrageparameter).
3. **Edge Cases**
   - Kein GPS in EXIF: zeige Info „Keine Standortdaten gefunden“.
   - Offline/kein Netzwerk: Kartenansicht zeigt Platzhalter + CTA „Offline-Modus aktivieren“ (falls verfügbar).
   - Permission entzogen: Funktion deaktivieren und Opt-In zurücksetzen.
4. **Haftung & Datenschutz**
   - In den Opt-In-Texten klarstellen: Die App liest nur lokale EXIF-Daten und speichert sie lokal; der Nutzer ist verantwortlich, wenn Standortdaten sensibel sind.
   - Haftungshinweis aufnehmen: Für externe Tile-Server gelten deren Nutzungsbedingungen/Datenschutzerklärungen; die App haftet nicht für Verfügbarkeit, Inhalte oder Datenerhebung Dritter.
   - Ergänze Links/Verweise in der UI, sofern im Projekt üblich (z. B. `SettingsPage.xaml`).
5. **Erweiterungen**
   - Option „Locations komplett deaktivieren“ mit Hard-Disable in `MapViewModel` und im Indexer.
   - Optionaler Hinweis auf Offline-Tile-Cache, sofern implementiert (Feature aus Punkt 6).
