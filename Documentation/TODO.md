# TODOs – Verbesserungen am aktuellen Code

> Fokus: Stabilität, Performance, Datenqualität und Wartbarkeit basierend auf dem aktuellen Stand des Repos.

## Indexing & Datenqualität
- [ ] **Gelöschte/verschobene Dateien zuverlässig bereinigen:** Beim Index-Lauf pro Quelle fehlende Dateien entfernen (z. B. `IndexService.RemoveAsync` + Vergleich gegen Scan-Ergebnis) und optional „Soft-Delete“-Markierung nutzen, um UI-Flicker zu vermeiden.
- [ ] **Inkrementelles Indexing:** `MediaStoreScanner` und Windows-Scanner um „Last-Modified“-Check erweitern und im DB-Schema ein `LastModifiedSeconds`/`FileSizeBytes` Feld ergänzen, um nur geänderte Dateien neu zu lesen.
- [ ] **Stabilere DB-Migrationen:** In `Services/AppDb.cs` von try/catch-ALTERs auf ein versionsbasiertes Migrationssystem umstellen (Schema-Version in eigener Tabelle).
- [ ] **Besseres Suchverhalten:** SQLite FTS (z. B. `FTS5`) für `MediaItem.Name` und `PersonTag.PersonName` integrieren, inkl. normalisierter Suche (case-/diacritics-insensitive).
- [ ] **Mehr Metadaten aufnehmen:** Dauer/Dimensionen für Photos/Videos im Index speichern und als Filter verfügbar machen.

## Performance & Cache

## UI/UX

## Plattform-spezifisch
- [ ] **Android Dateisystem-Operationen:** Für SAF-Quellen Copy/Move/Delete über `DocumentFile` unterstützen (aktuell nur Windows).
- [ ] **Map Offline Mode/Tile Cache:** Optionaler Offline-Cache, damit die Kartenansicht ohne Netz weiter nutzbar bleibt.

## Tests & Qualitätssicherung
- [ ] **Unit-Tests für Indexing/Query:** z. B. `IndexService.BuildQuery`/`AlbumService.BuildAlbumQuery`/`PeopleTagService`.
- [ ] **Smoke-Tests für People-Model-Download:** Falls möglich, logische Tests über `ModelFileService` (mocked FS/HTTP).
