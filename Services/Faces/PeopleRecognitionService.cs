using System.Diagnostics;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;
using UltimateVideoBrowser.Models;
using ImageSharpImage = SixLabors.ImageSharp.Image;
using System.Runtime.InteropServices;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace UltimateVideoBrowser.Services.Faces;

public sealed class PeopleRecognitionService
{
    private const float DefaultMatchThreshold = 0.36f; 
    private static float DefaultMinScore = 0.65f;

    private readonly AppDb db;
    private readonly YuNetFaceDetector faceDetector;
    private readonly SFaceRecognizer faceRecognizer;
    private readonly ModelFileService modelFiles;
    private readonly PeopleTagService peopleTagService;

    public PeopleRecognitionService(
        AppDb db,
        PeopleTagService peopleTagService,
        ModelFileService modelFiles,
        YuNetFaceDetector faceDetector,
        SFaceRecognizer faceRecognizer)
    {
        this.db = db;
        this.peopleTagService = peopleTagService;
        this.modelFiles = modelFiles;
        this.faceDetector = faceDetector;
        this.faceRecognizer = faceRecognizer;
    }

    public bool IsRuntimeLoaded => faceDetector.IsLoaded && faceRecognizer.IsLoaded;

    public string? LastModelLoadError { get; private set; }

    public async Task<bool> WarmupModelsAsync(CancellationToken ct)
    {
        try
        {
            LastModelLoadError = null;
            await faceDetector.EnsureLoadedAsync(ct).ConfigureAwait(false);
            await faceRecognizer.EnsureLoadedAsync(ct).ConfigureAwait(false);
            Debug.WriteLine($"[PeopleRecognition] WarmupModelsAsync OK. Loaded={IsRuntimeLoaded}");
            return IsRuntimeLoaded;
        }
        catch (Exception ex)
        {
            LastModelLoadError = ex.Message;
            Debug.WriteLine($"[PeopleRecognition] WarmupModelsAsync FAILED: {ex}");
            return false;
        }
    }

    public async Task<IReadOnlyList<FaceMatch>> EnsurePeopleTagsForMediaAsync(MediaItem item, CancellationToken ct)
    {
        if (item == null || item.MediaType != MediaType.Photos || string.IsNullOrWhiteSpace(item.Path))
            return Array.Empty<FaceMatch>();

        Debug.WriteLine($"[PeopleRecognition] EnsurePeopleTagsForMediaAsync: {item.Path}");

        try
        {
            ct.ThrowIfCancellationRequested();

            await db.EnsureInitializedAsync().ConfigureAwait(false);

            // Best-effort model warmup to avoid silent "0 faces" when runtime isn't loaded yet.
            // This keeps the call resilient: if models can't load, we continue and just return no matches.
            if (!IsRuntimeLoaded)
            {
                Debug.WriteLine("[PeopleRecognition] Runtime not loaded. Warming up...");
                await WarmupModelsAsync(ct).ConfigureAwait(false);
                Debug.WriteLine(
                    $"[PeopleRecognition] Runtime loaded={IsRuntimeLoaded}, LastError={LastModelLoadError ?? "<none>"}");
            }

            // Avoid re-scanning if the detector model hasn't changed since the last scan.
            var currentDetectionKey = await modelFiles.GetYuNetModelKeyAsync(ct).ConfigureAwait(false);
            if (!string.Equals(item.FaceScanModelKey, currentDetectionKey, StringComparison.OrdinalIgnoreCase))
            {
                // Model changed (or never scanned). Drop stale detections so we can rebuild deterministically.
                await db.Db.ExecuteAsync("DELETE FROM FaceEmbedding WHERE MediaPath = ?;", item.Path)
                    .ConfigureAwait(false);
                item.FaceScanModelKey = null;
                item.FaceScanAtSeconds = 0;
            }

            var currentEmbeddingKey = await modelFiles.GetSFaceModelKeyAsync(ct).ConfigureAwait(false);

            var embeddings = await db.Db.Table<FaceEmbedding>()
                .Where(face => face.MediaPath == item.Path)
                .OrderBy(face => face.FaceIndex)
                .ToListAsync()
                .ConfigureAwait(false);

            var needsEmbeddingRefresh = embeddings.Any(e =>
                string.IsNullOrWhiteSpace(e.EmbeddingModelKey) ||
                !string.Equals(e.EmbeddingModelKey, currentEmbeddingKey, StringComparison.OrdinalIgnoreCase));

            if (needsEmbeddingRefresh)
            {
                await db.Db.ExecuteAsync("DELETE FROM FaceEmbedding WHERE MediaPath = ?;", item.Path)
                    .ConfigureAwait(false);
                embeddings.Clear();
                item.FaceScanModelKey = null;
                item.FaceScanAtSeconds = 0;
            }

            if (embeddings.Count == 0)
            {
                embeddings = await DetectAndStoreFacesAsync(item.Path, currentDetectionKey, ct).ConfigureAwait(false);

                item.FaceScanModelKey = currentDetectionKey;
                item.FaceScanAtSeconds = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
                await db.Db.UpdateAsync(item).ConfigureAwait(false);

                Debug.WriteLine($"[PeopleRecognition] Detected faces: {embeddings.Count} for {item.Path}");
            }
            else
            {
                await TryUpdateFaceBoxesAsync(item.Path, embeddings, ct).ConfigureAwait(false);
                Debug.WriteLine($"[PeopleRecognition] Reused stored faces: {embeddings.Count} for {item.Path}");
            }

            if (embeddings.Count == 0)
                return Array.Empty<FaceMatch>();

            var people = await db.Db.Table<PersonProfile>().ToListAsync().ConfigureAwait(false);
            var updatedPersonIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            var knownEmbeddings = await db.Db.Table<FaceEmbedding>()
                .Where(face => face.PersonId != null && face.PersonId != "")
                .ToListAsync()
                .ConfigureAwait(false);

            var peopleMap = people.ToDictionary(person => person.Id, person => person);
            var personEmbeddings = BuildEmbeddingMap(knownEmbeddings);

            foreach (var face in embeddings.Where(face => string.IsNullOrWhiteSpace(face.PersonId)))
            {
                ct.ThrowIfCancellationRequested();

                var embedding = BytesToFloats(face.Embedding);
                var match = FindBestMatch(embedding, personEmbeddings);

                if (match.PersonId == null || match.Similarity < DefaultMatchThreshold)
                {
                    var newPerson = CreateUnknownPerson(peopleMap.Values);
                    await db.Db.InsertAsync(newPerson).ConfigureAwait(false);

                    peopleMap[newPerson.Id] = newPerson;
                    personEmbeddings[newPerson.Id] = new List<float[]> { embedding };

                    face.PersonId = newPerson.Id;
                    updatedPersonIds.Add(newPerson.Id);
                }
                else
                {
                    face.PersonId = match.PersonId;
                    updatedPersonIds.Add(match.PersonId!);

                    if (personEmbeddings.TryGetValue(match.PersonId, out var list))
                        list.Add(embedding);
                }

                await db.Db.UpdateAsync(face).ConfigureAwait(false);
            }

            foreach (var pid in updatedPersonIds)
                await RecomputePersonQualityAsync(pid).ConfigureAwait(false);

            var matches = embeddings
                .Where(face => !string.IsNullOrWhiteSpace(face.PersonId))
                .Select((face, index) =>
                {
                    var person = peopleMap[face.PersonId!];
                    var embedding = BytesToFloats(face.Embedding);
                    var similarity = ComputeBestSimilarityToPerson(face.PersonId!, embedding, personEmbeddings);
                    return new FaceMatch(person.Id, person.Name, similarity, index);
                })
                .ToList();

            await UpdateMediaTagsAsync(item.Path, matches.Select(match => match.PersonId), peopleMap.Values)
                .ConfigureAwait(false);

            Debug.WriteLine($"[PeopleRecognition] Matches: {matches.Count} for {item.Path}");
            return matches;
        }
        catch (Exception ex)
        {
            LastModelLoadError = ex.Message;
            Debug.WriteLine($"[PeopleRecognition] EnsurePeopleTagsForMediaAsync FAILED: {ex}");
            return Array.Empty<FaceMatch>();
        }
    }

    public async Task RenamePersonAsync(string personId, string newName, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(personId))
            return;

        var trimmed = (newName ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(trimmed))
            return;

        await db.EnsureInitializedAsync().ConfigureAwait(false);
        ct.ThrowIfCancellationRequested();

        var people = await db.Db.Table<PersonProfile>().ToListAsync().ConfigureAwait(false);
        var current = people.FirstOrDefault(p => p.Id == personId);
        if (current == null)
            return;

        var existing = people.FirstOrDefault(p =>
            p.Id != current.Id &&
            string.Equals(p.Name, trimmed, StringComparison.OrdinalIgnoreCase));

        if (existing != null)
        {
            await MergePersonAsync(current.Id, existing.Id).ConfigureAwait(false);
            await UpdateMediaTagsForPersonAsync(existing).ConfigureAwait(false);
            return;
        }

        if (string.Equals(current.Name, trimmed, StringComparison.OrdinalIgnoreCase))
            return;

        current.Name = trimmed;
        current.UpdatedUtc = DateTimeOffset.UtcNow;
        await db.Db.UpdateAsync(current).ConfigureAwait(false);
        await UpdateMediaTagsForPersonAsync(current).ConfigureAwait(false);
    }

    public async Task RenamePeopleForMediaAsync(MediaItem item, IReadOnlyList<string> names, CancellationToken ct)
    {
        if (item == null || item.MediaType != MediaType.Photos || string.IsNullOrWhiteSpace(item.Path))
            return;

        await db.EnsureInitializedAsync().ConfigureAwait(false);

        var currentEmbeddingKey = await modelFiles.GetSFaceModelKeyAsync(ct).ConfigureAwait(false);

        var embeddings = await db.Db.Table<FaceEmbedding>()
            .Where(face => face.MediaPath == item.Path)
            .OrderBy(face => face.FaceIndex)
            .ToListAsync()
            .ConfigureAwait(false);

        var needsEmbeddingRefresh = embeddings.Any(e =>
            string.IsNullOrWhiteSpace(e.EmbeddingModelKey) ||
            !string.Equals(e.EmbeddingModelKey, currentEmbeddingKey, StringComparison.OrdinalIgnoreCase));

        if (needsEmbeddingRefresh)
        {
            await db.Db.ExecuteAsync("DELETE FROM FaceEmbedding WHERE MediaPath = ?;", item.Path).ConfigureAwait(false);
            embeddings.Clear();
            item.FaceScanModelKey = null;
            item.FaceScanAtSeconds = 0;
        }

        if (embeddings.Count == 0)
            return;

        var desiredByPerson = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < embeddings.Count && i < names.Count; i++)
        {
            var name = (names[i] ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(name))
                continue;

            var face = embeddings[i];
            if (string.IsNullOrWhiteSpace(face.PersonId))
                continue;

            desiredByPerson[face.PersonId] = name;
        }

        foreach (var (personId, desired) in desiredByPerson)
        {
            ct.ThrowIfCancellationRequested();
            await RenamePersonAsync(personId, desired, ct).ConfigureAwait(false);
        }

        var updatedPeople = await db.Db.Table<PersonProfile>().ToListAsync().ConfigureAwait(false);
        await UpdateMediaTagsAsync(item.Path, await GetPersonIdsForMediaAsync(item.Path).ConfigureAwait(false),
                updatedPeople)
            .ConfigureAwait(false);
    }

    public async Task ScanAndTagAsync(
        IEnumerable<MediaItem> items,
        IProgress<(int processed, int total, string path)>? progress,
        CancellationToken ct)
    {
        var list = items.Where(item => item.MediaType == MediaType.Photos && !string.IsNullOrWhiteSpace(item.Path))
            .ToList();

        var total = list.Count;
        var processed = 0;

        foreach (var item in list)
        {
            ct.ThrowIfCancellationRequested();
            await EnsurePeopleTagsForMediaAsync(item, ct).ConfigureAwait(false);
            processed++;
            progress?.Report((processed, total, item.Path));
        }
    }

    private async Task<List<FaceEmbedding>> DetectAndStoreFacesAsync(string path, string detectionModelKey,
        CancellationToken ct)
    {
        if (!File.Exists(path))
            return new List<FaceEmbedding>();

        using var image = ImageSharpImage.Load<Rgba32>(path);
        image.Mutate(ctx => ctx.AutoOrient());

        var embeddingModelKey = await modelFiles.GetSFaceModelKeyAsync(ct).ConfigureAwait(false);

        IReadOnlyList<DetectedFace> faces;
        try
        {
            using var referenceImage = await TryLoadReferenceImageFromDesktopAsync(ct).ConfigureAwait(false);
            YuNetFaceDetector.YuNetTuning? tuning = YuNetFaceDetector.YuNetTuning.Default;

            //if (referenceImage != null)
            //{
            //    tuning = await faceDetector.CalibrateFromReferenceImageAsync(referenceImage, expectedFaces: 4, ct)
            //        .ConfigureAwait(false);
            //}

            // 2) Use tuned detection if available, otherwise fallback to your default.
            if (tuning != null)
            {
                faces = await faceDetector.DetectFacesAsync(image, tuning, ct).ConfigureAwait(false);
            }
            else
            {
                faces = await faceDetector.DetectFacesAsync(image, DefaultMinScore, ct).ConfigureAwait(false);
            }
        }
        catch (Exception ex)
        {
            LastModelLoadError = ex.Message;
            Debug.WriteLine($"[PeopleRecognition] DetectFacesAsync FAILED: {ex}");
            return new List<FaceEmbedding>();
        }

        if (faces.Count == 0)
            return new List<FaceEmbedding>();

        await db.Db.ExecuteAsync("DELETE FROM FaceEmbedding WHERE MediaPath = ?;", path).ConfigureAwait(false);

        var embeddings = new List<FaceEmbedding>();
        for (var i = 0; i < faces.Count; i++)
        {
            ct.ThrowIfCancellationRequested();
            var face = faces[i];

            float[] embedding;
            try
            {
                embedding = await faceRecognizer.ExtractEmbeddingAsync(image, face, ct).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                LastModelLoadError = ex.Message;
                Debug.WriteLine($"[PeopleRecognition] ExtractEmbeddingAsync FAILED (face {i}): {ex}");
                continue;
            }

            if (embedding.Length == 0)
                continue;

            var faceQuality = ComputeFaceQuality(face.Score, face.W, face.H);

            embeddings.Add(new FaceEmbedding
            {
                MediaPath = path,
                FaceIndex = i,
                Score = face.Score,
                X = face.X,
                Y = face.Y,
                W = face.W,
                H = face.H,
                ImageWidth = image.Width,
                ImageHeight = image.Height,
                DetectionModelKey = detectionModelKey,
                EmbeddingModelKey = embeddingModelKey,
                FaceQuality = faceQuality,
                Embedding = FloatsToBytes(embedding)
            });
        }

        foreach (var embedding in embeddings)
            await db.Db.InsertAsync(embedding).ConfigureAwait(false);

        return embeddings;
    }

    private async Task TryUpdateFaceBoxesAsync(string mediaPath, List<FaceEmbedding> embeddings, CancellationToken ct)
    {
        if (embeddings.Count == 0)
            return;

        var needsUpdate = embeddings.Any(e => e.W <= 0 || e.H <= 0 || e.ImageWidth <= 0 || e.ImageHeight <= 0);
        if (!needsUpdate)
            return;

        if (!File.Exists(mediaPath))
            return;

        try
        {
            using var image = ImageSharpImage.Load<Rgba32>(mediaPath);
            image.Mutate(ctx => ctx.AutoOrient());

            IReadOnlyList<DetectedFace> faces;
            try
            {
                faces = await faceDetector.DetectFacesAsync(image, DefaultMinScore, ct).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                LastModelLoadError = ex.Message;
                Debug.WriteLine($"[PeopleRecognition] TryUpdateFaceBoxes DetectFacesAsync FAILED: {ex}");
                return;
            }

            if (faces.Count == 0)
                return;

            var count = Math.Min(embeddings.Count, faces.Count);
            for (var i = 0; i < count; i++)
            {
                var face = faces[i];
                var row = embeddings[i];
                row.X = face.X;
                row.Y = face.Y;
                row.W = face.W;
                row.H = face.H;
                row.ImageWidth = image.Width;
                row.ImageHeight = image.Height;
                await db.Db.UpdateAsync(row).ConfigureAwait(false);
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[PeopleRecognition] TryUpdateFaceBoxes FAILED: {ex}");
        }
    }

    private async Task MergePersonAsync(string fromPersonId, string toPersonId)
    {
        if (string.IsNullOrWhiteSpace(fromPersonId) || string.IsNullOrWhiteSpace(toPersonId))
            return;

        if (string.Equals(fromPersonId, toPersonId, StringComparison.OrdinalIgnoreCase))
            return;

        await db.Db.ExecuteAsync(
                "UPDATE FaceEmbedding SET PersonId = ? WHERE PersonId = ?;",
                toPersonId,
                fromPersonId)
            .ConfigureAwait(false);

        try
        {
            await db.Db.DeleteAsync<PersonProfile>(fromPersonId).ConfigureAwait(false);
        }
        catch
        {
            // Best-effort delete.
        }
    }

    private async Task UpdateMediaTagsForPersonAsync(PersonProfile person)
    {
        var mediaPaths = await db.Db.Table<FaceEmbedding>()
            .Where(face => face.PersonId == person.Id)
            .ToListAsync()
            .ConfigureAwait(false);

        var people = await db.Db.Table<PersonProfile>().ToListAsync().ConfigureAwait(false);

        foreach (var path in mediaPaths.Select(face => face.MediaPath).Distinct(StringComparer.OrdinalIgnoreCase))
            await UpdateMediaTagsAsync(path, await GetPersonIdsForMediaAsync(path).ConfigureAwait(false), people)
                .ConfigureAwait(false);
    }

    private async Task UpdateMediaTagsAsync(string mediaPath, IEnumerable<string> personIds,
        IEnumerable<PersonProfile> people)
    {
        var names = personIds
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Select(id => people.FirstOrDefault(person => person.Id == id)?.Name)
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Select(name => name!)
            .ToList();

        await peopleTagService.SetTagsForMediaAsync(mediaPath, names).ConfigureAwait(false);
    }

    private async Task<List<string>> GetPersonIdsForMediaAsync(string mediaPath)
    {
        var faces = await db.Db.Table<FaceEmbedding>()
            .Where(face => face.MediaPath == mediaPath && face.PersonId != null && face.PersonId != "")
            .ToListAsync()
            .ConfigureAwait(false);

        return faces.Select(face => face.PersonId!).ToList();
    }

    private static Dictionary<string, List<float[]>> BuildEmbeddingMap(IEnumerable<FaceEmbedding> embeddings)
    {
        var map = new Dictionary<string, List<float[]>>(StringComparer.OrdinalIgnoreCase);

        foreach (var embedding in embeddings)
        {
            if (string.IsNullOrWhiteSpace(embedding.PersonId))
                continue;

            var vector = BytesToFloats(embedding.Embedding);
            if (!map.TryGetValue(embedding.PersonId, out var list))
            {
                list = new List<float[]>();
                map[embedding.PersonId] = list;
            }

            list.Add(vector);
        }

        return map;
    }

    private static (string? PersonId, float Similarity) FindBestMatch(float[] embedding,
        Dictionary<string, List<float[]>> personEmbeddings)
    {
        string? bestPerson = null;
        var bestSimilarity = float.NegativeInfinity;

        foreach (var (personId, embeddings) in personEmbeddings)
        foreach (var candidate in embeddings)
        {
            var similarity = SFaceRecognizer.CosineSimilarity(embedding, candidate);
            if (similarity > bestSimilarity)
            {
                bestSimilarity = similarity;
                bestPerson = personId;
            }
        }

        return (bestPerson, bestSimilarity);
    }

    private static float ComputeBestSimilarityToPerson(string personId, float[] embedding,
        Dictionary<string, List<float[]>> personEmbeddings)
    {
        if (!personEmbeddings.TryGetValue(personId, out var list) || list.Count == 0)
            return float.NegativeInfinity;

        var best = float.NegativeInfinity;
        foreach (var candidate in list)
        {
            var similarity = SFaceRecognizer.CosineSimilarity(embedding, candidate);
            if (similarity > best)
                best = similarity;
        }

        return best;
    }

    private static PersonProfile CreateUnknownPerson(IEnumerable<PersonProfile> people)
    {
        var max = 0;

        foreach (var person in people)
        {
            if (!person.Name.StartsWith("Unknown ", StringComparison.OrdinalIgnoreCase))
                continue;

            var suffix = person.Name["Unknown ".Length..];
            if (int.TryParse(suffix, out var value) && value > max)
                max = value;
        }

        return new PersonProfile
        {
            Name = $"Unknown {max + 1}",
            CreatedUtc = DateTimeOffset.UtcNow,
            UpdatedUtc = DateTimeOffset.UtcNow
        };
    }

    private static byte[] FloatsToBytes(float[] values)
    {
        var bytes = new byte[values.Length * sizeof(float)];
        Buffer.BlockCopy(values, 0, bytes, 0, bytes.Length);
        return bytes;
    }

    private static float[] BytesToFloats(byte[] bytes)
    {
        var values = new float[bytes.Length / sizeof(float)];
        Buffer.BlockCopy(bytes, 0, values, 0, bytes.Length);
        return values;
    }

    private static float ComputeFaceQuality(float detectorScore, float w, float h)
    {
        // A fast, deterministic quality heuristic in the 0..1 range.
        // We combine detector confidence with face size to prioritize sharp, usable crops.
        var s1 = Clamp01((detectorScore - 0.5f) / 0.5f);
        var minSide = MathF.Min(w, h);
        var s2 = Clamp01(minSide / 140f);
        // Weight detector score a bit more than size to keep false positives down.
        return Clamp01(0.6f * s1 + 0.4f * s2);
    }

    private async Task RecomputePersonQualityAsync(string personId)
    {
        if (string.IsNullOrWhiteSpace(personId))
            return;

        var faces = await db.Db.Table<FaceEmbedding>()
            .Where(f => f.PersonId == personId)
            .OrderByDescending(f => f.FaceQuality)
            .ToListAsync()
            .ConfigureAwait(false);

        var profile = await db.Db.Table<PersonProfile>()
            .Where(p => p.Id == personId)
            .FirstOrDefaultAsync()
            .ConfigureAwait(false);

        if (profile == null)
            return;

        const int k = 5;
        var take = Math.Min(k, faces.Count);
        var avg = 0f;

        for (var i = 0; i < take; i++)
            avg += faces[i].FaceQuality;

        avg = take > 0 ? avg / take : 0f;

        var countBonus = faces.Count > 0 ? Clamp01(MathF.Log10(faces.Count + 1) / 2f) * 0.1f : 0f;

        profile.QualityScore = Clamp01(avg + countBonus);
        profile.PrimaryFaceEmbeddingId = faces.FirstOrDefault()?.Id;
        profile.UpdatedUtc = DateTimeOffset.UtcNow;

        await db.Db.UpdateAsync(profile).ConfigureAwait(false);
    }

    private static float Clamp01(float v)
    {
        return v < 0f ? 0f : v > 1f ? 1f : v;
    }
    private static async Task<Image<Rgba32>?> TryLoadReferenceImageFromDesktopAsync(CancellationToken ct)
    {
        // Comments intentionally in English.
#if WINDOWS
    // 1) Try: Desktop\ref.jpg
    var desktop = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
    var p1 = Path.Combine(desktop, "ref.jpg");
    if (File.Exists(p1))
        return await ImageSharpImage.LoadAsync<Rgba32>(p1, ct).ConfigureAwait(false);

    // 2) Try: Desktop\ref.JPG (case variants)
    var p2 = Path.Combine(desktop, "ref.JPG");
    if (File.Exists(p2))
        return await ImageSharpImage.LoadAsync<Rgba32>(p2, ct).ConfigureAwait(false);

    // 3) Try: App base directory (next to exe)
    var appDir = AppContext.BaseDirectory;
    var p3 = Path.Combine(appDir, "ref.jpg");
    if (File.Exists(p3))
        return await ImageSharpImage.LoadAsync<Rgba32>(p3, ct).ConfigureAwait(false);

    var p4 = Path.Combine(appDir, "ref.JPG");
    if (File.Exists(p4))
        return await ImageSharpImage.LoadAsync<Rgba32>(p4, ct).ConfigureAwait(false);

    return null;
#else
        // Not supported for Android/iOS in this direct "desktop path" form.
        return null;
#endif
    }
}