using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;
using UltimateVideoBrowser.Models;
using ImageSharpImage = SixLabors.ImageSharp.Image;

namespace UltimateVideoBrowser.Services.Faces;

public sealed class PeopleRecognitionService
{
    private const float DefaultMatchThreshold = 0.36f;
    private const float DefaultMinScore = 0.6f;

    private readonly AppDb db;
    private readonly YuNetFaceDetector faceDetector;
    private readonly SFaceRecognizer faceRecognizer;
    private readonly PeopleTagService peopleTagService;

    public PeopleRecognitionService(
        AppDb db,
        PeopleTagService peopleTagService,
        YuNetFaceDetector faceDetector,
        SFaceRecognizer faceRecognizer)
    {
        this.db = db;
        this.peopleTagService = peopleTagService;
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
            return IsRuntimeLoaded;
        }
        catch (Exception ex)
        {
            LastModelLoadError = ex.Message;
            return false;
        }
    }

    public async Task<IReadOnlyList<FaceMatch>> EnsurePeopleTagsForMediaAsync(MediaItem item, CancellationToken ct)
    {
        if (item == null || item.MediaType != MediaType.Photos || string.IsNullOrWhiteSpace(item.Path))
            return Array.Empty<FaceMatch>();

        try
        {
            await db.EnsureInitializedAsync().ConfigureAwait(false);

            var embeddings = await db.Db.Table<FaceEmbedding>()
                .Where(face => face.MediaPath == item.Path)
                .OrderBy(face => face.FaceIndex)
                .ToListAsync()
                .ConfigureAwait(false);

            if (embeddings.Count == 0)
                embeddings = await DetectAndStoreFacesAsync(item.Path, ct).ConfigureAwait(false);
            else
                await TryUpdateFaceBoxesAsync(item.Path, embeddings, ct).ConfigureAwait(false);

            if (embeddings.Count == 0)
                return Array.Empty<FaceMatch>();

            var people = await db.Db.Table<PersonProfile>().ToListAsync().ConfigureAwait(false);
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
                }
                else
                {
                    face.PersonId = match.PersonId;
                    if (personEmbeddings.TryGetValue(match.PersonId, out var list))
                        list.Add(embedding);
                }

                await db.Db.UpdateAsync(face).ConfigureAwait(false);
            }

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
            return matches;
        }
        catch
        {
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

        var embeddings = await db.Db.Table<FaceEmbedding>()
            .Where(face => face.MediaPath == item.Path)
            .OrderBy(face => face.FaceIndex)
            .ToListAsync()
            .ConfigureAwait(false);

        if (embeddings.Count == 0)
            return;

        // Rename in a Picasa-like way: if the user types a name that already exists,
        // we merge the current person into the existing profile instead of creating duplicates.
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

        // Refresh tags for the current media after potential merges.
        var updatedPeople = await db.Db.Table<PersonProfile>().ToListAsync().ConfigureAwait(false);
        await UpdateMediaTagsAsync(item.Path, await GetPersonIdsForMediaAsync(item.Path).ConfigureAwait(false),
                updatedPeople)
            .ConfigureAwait(false);
    }

    public async Task ScanAndTagAsync(IEnumerable<MediaItem> items,
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

    private async Task<List<FaceEmbedding>> DetectAndStoreFacesAsync(string path, CancellationToken ct)
    {
        if (!File.Exists(path))
            return new List<FaceEmbedding>();

        using var image = ImageSharpImage.Load<Rgba32>(path);
        image.Mutate(ctx => ctx.AutoOrient());

        IReadOnlyList<DetectedFace> faces;
        try
        {
            faces = await faceDetector.DetectFacesAsync(image, DefaultMinScore, ct).ConfigureAwait(false);
        }
        catch
        {
            // If the model is missing or cannot be initialized (offline / download blocked),
            // keep browsing resilient and simply skip automatic face detection.
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
            catch
            {
                // Best-effort: skip this face if recognition is unavailable.
                continue;
            }

            if (embedding.Length == 0)
                continue;
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

        // If boxes were not stored yet (older DB), re-run detection once and patch the rows.
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
            catch
            {
                // If the model is missing or cannot be initialized (offline / download blocked),
                // keep browsing resilient and simply skip automatic face detection.
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
        catch
        {
            // Best-effort patching; do not break browsing.
        }
    }

    private async Task MergePersonAsync(string fromPersonId, string toPersonId)
    {
        if (string.IsNullOrWhiteSpace(fromPersonId) || string.IsNullOrWhiteSpace(toPersonId))
            return;

        if (string.Equals(fromPersonId, toPersonId, StringComparison.OrdinalIgnoreCase))
            return;

        // Re-assign all embeddings to the target person.
        await db.Db.ExecuteAsync(
                "UPDATE FaceEmbedding SET PersonId = ? WHERE PersonId = ?;",
                toPersonId,
                fromPersonId)
            .ConfigureAwait(false);

        // Remove the source profile if it exists.
        try
        {
            await db.Db.DeleteAsync<PersonProfile>(fromPersonId).ConfigureAwait(false);
        }
        catch
        {
            // Ignore; profile might already be gone.
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
}