#if ANDROID && !WINDOWS
using Android.Content;
using Android.Database;
using Android.Media;
using Android.Provider;
using AndroidX.DocumentFile.Provider;
using Uri = Android.Net.Uri;
#elif WINDOWS
using Windows.Storage;
using Windows.Storage.AccessCache;
using Windows.Storage.Search;
#endif
using System.Diagnostics;
using System.Runtime.CompilerServices;
using UltimateVideoBrowser.Helpers;
using UltimateVideoBrowser.Models;
using FileAttributes = System.IO.FileAttributes;
using IOPath = System.IO.Path;
#if !ANDROID || WINDOWS
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Metadata.Profiles.Exif;
using ImageSharpImage = SixLabors.ImageSharp.Image;
#endif
#if WINDOWS
using System.Threading.Channels;
#endif
using ModelMediaType = UltimateVideoBrowser.Models.MediaType;

namespace UltimateVideoBrowser.Services;

public sealed class MediaStoreScanner
{
    private readonly AppSettingsService settingsService;

    public MediaStoreScanner(AppSettingsService settingsService)
    {
        this.settingsService = settingsService;
    }

    public async IAsyncEnumerable<MediaItem> StreamSourceAsync(MediaSource source, ModelMediaType indexedTypes,
        [EnumeratorCancellation] CancellationToken ct)
    {
        var sourceId = source.Id;
        var rootPath = source.LocalFolderPath ?? "";
        var extensions = new ExtensionLookup(settingsService);
#if ANDROID && !WINDOWS
        if (string.IsNullOrWhiteSpace(rootPath))
        {
            foreach (var item in ScanMediaStore(sourceId, indexedTypes, extensions, ct))
                yield return item;
            yield break;
        }

        foreach (var item in ScanAndroidFolder(rootPath, sourceId, indexedTypes, extensions, ct))
            yield return item;
#elif WINDOWS
        if (string.IsNullOrWhiteSpace(rootPath))
            foreach (var defaultRoot in GetWindowsDefaultRoots(indexedTypes))
                await foreach (var item in ScanWindowsAsync(defaultRoot, sourceId, source.AccessToken, indexedTypes,
                                   extensions, ct))
                    yield return item;
        else
            await foreach (var item in ScanWindowsAsync(rootPath, sourceId, source.AccessToken, indexedTypes,
                               extensions, ct))
                yield return item;
#else
        _ = sourceId;
        _ = rootPath;
        _ = extensions;
        await Task.CompletedTask;
#endif
    }

    public async Task<int> CountSourceAsync(MediaSource source, ModelMediaType indexedTypes, CancellationToken ct)
    {
        var rootPath = source.LocalFolderPath ?? "";
        var extensions = new ExtensionLookup(settingsService);
#if ANDROID && !WINDOWS
        if (string.IsNullOrWhiteSpace(rootPath))
            return CountMediaStore(indexedTypes, extensions, ct);

        return CountAndroidFolder(rootPath, indexedTypes, extensions, ct);
#elif WINDOWS
        if (string.IsNullOrWhiteSpace(rootPath))
        {
            var total = 0;
            foreach (var defaultRoot in GetWindowsDefaultRoots(indexedTypes))
                total += await CountWindowsAsync(defaultRoot, source.AccessToken, indexedTypes, extensions, ct)
                    .ConfigureAwait(false);
            return total;
        }

        return await CountWindowsAsync(rootPath, source.AccessToken, indexedTypes, extensions, ct)
            .ConfigureAwait(false);
#else
        _ = rootPath;
        _ = extensions;
        _ = ct;
        return 0;
#endif
    }

    private sealed class ExtensionLookup
    {
        private readonly IReadOnlySet<string> documentExtensions;
        private readonly IReadOnlySet<string> photoExtensions;
        private readonly IReadOnlySet<string> videoExtensions;

        public ExtensionLookup(AppSettingsService settingsService)
        {
            videoExtensions = settingsService.GetVideoExtensions();
            photoExtensions = settingsService.GetPhotoExtensions();
            documentExtensions = settingsService.GetDocumentExtensions();
        }

        public ModelMediaType GetMediaTypeFromPath(string? path)
        {
            return GetMediaType(Path.GetExtension(path ?? string.Empty));
        }

        public ModelMediaType GetMediaTypeFromName(string? name)
        {
            return GetMediaType(Path.GetExtension(name ?? string.Empty));
        }

        public bool IsCandidatePath(string? path, ModelMediaType indexedTypes)
        {
            var ext = Path.GetExtension(path ?? string.Empty);
            return IsCandidateExtension(ext, indexedTypes);
        }

        public bool IsCandidateName(string? name, ModelMediaType indexedTypes)
        {
            var ext = Path.GetExtension(name ?? string.Empty);
            return IsCandidateExtension(ext, indexedTypes);
        }

        public bool IsPhotoName(string? name)
        {
            var ext = Path.GetExtension(name ?? string.Empty);
            return !string.IsNullOrWhiteSpace(ext) && photoExtensions.Contains(ext);
        }

        public bool IsDocumentName(string? name)
        {
            var ext = Path.GetExtension(name ?? string.Empty);
            return !string.IsNullOrWhiteSpace(ext) && documentExtensions.Contains(ext);
        }

        private bool IsCandidateExtension(string? ext, ModelMediaType indexedTypes)
        {
            if (string.IsNullOrWhiteSpace(ext))
                return false;

            if (videoExtensions.Contains(ext))
                return indexedTypes.HasFlag(ModelMediaType.Videos);
            if (photoExtensions.Contains(ext))
                return indexedTypes.HasFlag(ModelMediaType.Photos) || indexedTypes.HasFlag(ModelMediaType.Graphics);
            if (documentExtensions.Contains(ext))
                return indexedTypes.HasFlag(ModelMediaType.Documents);

            return false;
        }

        private ModelMediaType GetMediaType(string? ext)
        {
            if (string.IsNullOrWhiteSpace(ext))
                return ModelMediaType.None;

            if (videoExtensions.Contains(ext))
                return ModelMediaType.Videos;
            if (photoExtensions.Contains(ext))
                return ModelMediaType.Photos;
            if (documentExtensions.Contains(ext))
                return ModelMediaType.Documents;
            return ModelMediaType.None;
        }
    }

    private static readonly string[] ThumbnailFolderTokens =
    {
        "thumbnails",
        "thumbs",
        ".thumbnails"
    };

    private const long PhotoSizeThresholdBytes = 256 * 1024;

    private static void LogScanEntry(string? path, string? name, string source, string result,
        ModelMediaType mediaType)
    {
        if (string.IsNullOrWhiteSpace(path))
            return;

        ScanLog.LogScan(path, name, source, result, (ModelMediaType)mediaType);
    }

    private static ModelMediaType GuessMediaType(string? path, string? name, ExtensionLookup extensions)
    {
        if (!string.IsNullOrWhiteSpace(name))
            return extensions.GetMediaTypeFromName(name);

        if (!string.IsNullOrWhiteSpace(path))
            return extensions.GetMediaTypeFromPath(path);

        return ModelMediaType.None;
    }

    private static bool IsThumbnailPath(string? path, string? name)
    {
        var candidate = path ?? name ?? string.Empty;
        if (string.IsNullOrWhiteSpace(candidate))
            return false;

        var normalized = candidate.Replace('\\', '/');
        var segments = normalized.Split('/', StringSplitOptions.RemoveEmptyEntries);
        foreach (var segment in segments)
            if (ThumbnailFolderTokens.Any(token => segment.Equals(token, StringComparison.OrdinalIgnoreCase)))
                return true;

        var fileName = name ?? IOPath.GetFileName(candidate);
        return !string.IsNullOrWhiteSpace(fileName) &&
               fileName.Contains("thumb", StringComparison.OrdinalIgnoreCase);
    }

    private static ModelMediaType ResolveMediaTypeFromPath(string path, string? name, ModelMediaType indexedTypes,
        ExtensionLookup extensions, string? mimeType = null, long? sizeBytes = null)
    {
        if (IsThumbnailPath(path, name))
            return ModelMediaType.None;

        var baseType = GetMediaTypeFromMimeType(mimeType);
        if (baseType == ModelMediaType.None)
            baseType = string.IsNullOrWhiteSpace(name)
                ? extensions.GetMediaTypeFromPath(path)
                : extensions.GetMediaTypeFromName(name);
        return baseType switch
        {
            ModelMediaType.Videos when indexedTypes.HasFlag(ModelMediaType.Videos) => ModelMediaType.Videos,
            ModelMediaType.Documents when indexedTypes.HasFlag(ModelMediaType.Documents) => ModelMediaType.Documents,
            ModelMediaType.Photos => ResolveImageMediaType(path, indexedTypes, sizeBytes),
            _ => ModelMediaType.None
        };
    }

    private static ModelMediaType ResolveImageMediaType(string path, ModelMediaType indexedTypes, long? sizeBytes)
    {
        var wantsPhotos = indexedTypes.HasFlag(ModelMediaType.Photos);
        var wantsGraphics = indexedTypes.HasFlag(ModelMediaType.Graphics);
        if (!wantsPhotos && !wantsGraphics)
            return ModelMediaType.None;

        var contentSize = sizeBytes ?? TryGetFileSizeBytes(path);
        if (contentSize.HasValue && contentSize.Value <= 0)
        {
            if (path.StartsWith("content://", StringComparison.OrdinalIgnoreCase))
                contentSize = null;
            else
                return ModelMediaType.None;
        }

        if (wantsPhotos && !wantsGraphics)
            return ModelMediaType.Photos;

        if (!wantsPhotos && wantsGraphics)
            return ModelMediaType.Graphics;

        if (contentSize.HasValue && contentSize.Value >= PhotoSizeThresholdBytes)
            return ModelMediaType.Photos;

        if (HasCameraExif(path))
            return ModelMediaType.Photos;

        return ModelMediaType.Graphics;
    }

    private static ModelMediaType GetMediaTypeFromMimeType(string? mimeType)
    {
        if (string.IsNullOrWhiteSpace(mimeType))
            return ModelMediaType.None;

        if (mimeType.StartsWith("video/", StringComparison.OrdinalIgnoreCase))
            return ModelMediaType.Videos;
        if (mimeType.StartsWith("image/", StringComparison.OrdinalIgnoreCase))
            return ModelMediaType.Photos;
        if (mimeType.StartsWith("application/", StringComparison.OrdinalIgnoreCase) ||
            mimeType.StartsWith("text/", StringComparison.OrdinalIgnoreCase) ||
            mimeType.StartsWith("message/", StringComparison.OrdinalIgnoreCase))
            return ModelMediaType.Documents;

        return ModelMediaType.None;
    }

    private static long? TryGetFileSizeBytes(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return null;

#if ANDROID && !WINDOWS
        if (path.StartsWith("content://", StringComparison.OrdinalIgnoreCase))
            return TryGetContentUriSizeBytes(path);
#endif

        try
        {
            return new FileInfo(path).Length;
        }
        catch
        {
            return null;
        }
    }

    private static bool HasCameraExif(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return false;

#if ANDROID && !WINDOWS
        if (path.StartsWith("content://", StringComparison.OrdinalIgnoreCase))
            return HasCameraExifFromAndroidContent(path);

        return HasCameraExifFromAndroidFile(path);
#else
        return HasCameraExifFromImageSharp(path);
#endif
    }

#if ANDROID && !WINDOWS
    private static long? TryGetContentUriSizeBytes(string contentUri)
    {
        try
        {
            var resolver = Platform.AppContext?.ContentResolver;
            if (resolver == null)
                return null;

            var uri = Uri.Parse(contentUri);
            string[] projection = { IOpenableColumns.Size };
            using var cursor = resolver.Query(uri, projection, null, null, null);
            if (cursor == null)
                return null;

            var sizeCol = cursor.GetColumnIndex(IOpenableColumns.Size);
            if (sizeCol < 0 || !cursor.MoveToFirst() || cursor.IsNull(sizeCol))
                return null;

            return cursor.GetLong(sizeCol);
        }
        catch
        {
            return null;
        }
    }

    private static bool HasCameraExifFromAndroidContent(string contentUri)
    {
        try
        {
            var resolver = Platform.AppContext?.ContentResolver;
            if (resolver == null)
                return false;

            var uri = Uri.Parse(contentUri);
            using var stream = resolver.OpenInputStream(uri);
            if (stream == null)
                return false;

            var exif = new ExifInterface(stream);
            return HasCameraExifFromExifInterface(exif);
        }
        catch
        {
            return false;
        }
    }

    private static bool HasCameraExifFromAndroidFile(string path)
    {
        try
        {
            if (!File.Exists(path))
                return false;

            using var stream = File.OpenRead(path);
            var exif = new ExifInterface(stream);
            return HasCameraExifFromExifInterface(exif);
        }
        catch
        {
            return false;
        }
    }

    private static bool HasCameraExifFromExifInterface(ExifInterface exif)
    {
        try
        {
            return !string.IsNullOrWhiteSpace(exif.GetAttribute(ExifInterface.TagMake)) ||
                   !string.IsNullOrWhiteSpace(exif.GetAttribute(ExifInterface.TagModel)) ||
                   !string.IsNullOrWhiteSpace(exif.GetAttribute("DateTimeOriginal")) ||
                   !string.IsNullOrWhiteSpace(exif.GetAttribute("DateTime"));
        }
        catch
        {
            return false;
        }
    }
#else
    private static bool HasCameraExifFromImageSharp(string path)
    {
        try
        {
            if (!File.Exists(path))
                return false;

            using var stream = File.OpenRead(path);
            var info = ImageSharpImage.Identify(stream);
            return HasCameraExifProfile(info?.Metadata.ExifProfile);
        }
        catch
        {
            return false;
        }
    }

    private static bool HasCameraExifProfile(ExifProfile? profile)
    {
        if (profile == null)
            return false;

        return HasExifStringValue(profile, ExifTag.Make) ||
               HasExifStringValue(profile, ExifTag.Model) ||
               HasExifStringValue(profile, ExifTag.DateTimeOriginal) ||
               HasExifStringValue(profile, ExifTag.DateTimeDigitized);
    }

    private static bool HasExifStringValue(ExifProfile profile, ExifTag<string> tag)
    {
        if (!profile.TryGetValue(tag, out var value))
            return false;

        return !string.IsNullOrWhiteSpace(value?.Value);
    }
#endif

#if WINDOWS
    private static IEnumerable<string> GetWindowsDefaultRoots(ModelMediaType indexedTypes)
    {
        var roots = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (indexedTypes.HasFlag(ModelMediaType.Videos))
            roots.Add(Environment.GetFolderPath(Environment.SpecialFolder.MyVideos));
        if (indexedTypes.HasFlag(ModelMediaType.Photos) || indexedTypes.HasFlag(ModelMediaType.Graphics))
            roots.Add(Environment.GetFolderPath(Environment.SpecialFolder.MyPictures));
        if (indexedTypes.HasFlag(ModelMediaType.Documents))
            roots.Add(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments));
        return roots.Where(root => !string.IsNullOrWhiteSpace(root));
    }

    private static async IAsyncEnumerable<MediaItem> ScanWindowsAsync(string? rootPath, string? sourceId,
        string? accessToken, ModelMediaType indexedTypes, ExtensionLookup extensions,
        [EnumeratorCancellation] CancellationToken ct)
    {
        var root = rootPath;

        if (string.IsNullOrWhiteSpace(root))
            yield break;

        if (Directory.Exists(root))
        {
            await foreach (var item in ScanWindowsFileSystemAsync(root, sourceId, indexedTypes, extensions, ct))
                yield return item;
            yield break;
        }

        StorageFolder? folder = null;
        try
        {
            folder = await TryGetStorageFolderAsync(root, accessToken);
        }
        catch
        {
            folder = null;
        }

        if (folder != null)
        {
            await foreach (var item in ScanWindowsFolderAsync(folder, sourceId, indexedTypes, extensions, ct))
                yield return item;
        }
    }

    private static async Task<int> CountWindowsAsync(string? rootPath, string? accessToken,
        ModelMediaType indexedTypes, ExtensionLookup extensions, CancellationToken ct)
    {
        var root = rootPath;
        if (string.IsNullOrWhiteSpace(root))
            return 0;

        if (Directory.Exists(root))
        {
            var total = 0;
            foreach (var path in EnumerateMediaFilesStreamingWindows(root, indexedTypes, extensions, ct))
                if (ResolveMediaTypeFromPath(path, null, indexedTypes, extensions) != ModelMediaType.None)
                    total++;

            return total;
        }

        StorageFolder? folder = null;
        try
        {
            folder = await TryGetStorageFolderAsync(root, accessToken);
        }
        catch
        {
            folder = null;
        }

        if (folder != null)
            return await CountWindowsFolderAsync(folder, indexedTypes, extensions, ct).ConfigureAwait(false);

        return 0;
    }

    private const uint StorageQueryPageSize = 256;

    private static async Task<int> CountWindowsFolderAsync(StorageFolder root, ModelMediaType indexedTypes,
        ExtensionLookup extensions, CancellationToken ct)
    {
        var queue = new Queue<StorageFolder>();
        queue.Enqueue(root);
        var total = 0;

        while (queue.Count > 0)
        {
            ct.ThrowIfCancellationRequested();
            var folder = queue.Dequeue();

            await foreach (var file in EnumerateStorageFilesAsync(folder, ct))
            {
                ct.ThrowIfCancellationRequested();
                if (!extensions.IsCandidateName(file.Name, indexedTypes))
                {
                    LogScanEntry(file.Path, file.Name, "Windows.StorageFolder", "Skipped: extension filtered",
                        ModelMediaType.None);
                    continue;
                }

                var path = file.Path;
                if (string.IsNullOrWhiteSpace(path) ||
                    !string.Equals(IOPath.GetFileName(path), file.Name, StringComparison.OrdinalIgnoreCase))
                {
                    var folderPath = folder.Path;
                    if (!string.IsNullOrWhiteSpace(folderPath))
                        path = IOPath.Combine(folderPath, file.Name);
                }

                if (string.IsNullOrWhiteSpace(path))
                    continue;

                if (ResolveMediaTypeFromPath(path, file.Name, indexedTypes, extensions) != ModelMediaType.None)
                    total++;
            }

            await foreach (var subfolder in EnumerateStorageFoldersAsync(folder, ct))
                queue.Enqueue(subfolder);
        }

        return total;
    }

    private static IEnumerable<string> EnumerateMediaFilesStreamingWindows(string root, ModelMediaType indexedTypes,
        ExtensionLookup extensions, CancellationToken ct)
    {
        var queue = new Queue<string>();
        queue.Enqueue(root);

        while (queue.Count > 0)
        {
            ct.ThrowIfCancellationRequested();

            var dir = queue.Dequeue();

            IEnumerable<string> files;
            try
            {
                files = Directory.EnumerateFiles(dir, "*.*", new EnumerationOptions
                {
                    RecurseSubdirectories = false,
                    IgnoreInaccessible = true
                });
            }
            catch
            {
                files = Enumerable.Empty<string>();
            }

            foreach (var file in files)
            {
                ct.ThrowIfCancellationRequested();
                if (!extensions.IsCandidatePath(file, indexedTypes))
                {
                    LogScanEntry(file, null, "Windows.FileSystem", "Skipped: extension filtered",
                        ModelMediaType.None);
                    continue;
                }

                if (IsThumbnailPath(file, null))
                {
                    LogScanEntry(file, null, "Windows.FileSystem", "Skipped: thumbnail path", ModelMediaType.None);
                    continue;
                }

                var candidateType = GuessMediaType(file, null, extensions);
                LogScanEntry(file, null, "Windows.FileSystem", "Candidate", candidateType);
                yield return file;
            }

            IEnumerable<string> subdirs;
            try
            {
                subdirs = Directory.EnumerateDirectories(dir, "*", new EnumerationOptions
                {
                    RecurseSubdirectories = false,
                    IgnoreInaccessible = true
                });
            }
            catch
            {
                continue;
            }

            foreach (var sub in subdirs)
            {
                ct.ThrowIfCancellationRequested();
                queue.Enqueue(sub);
            }
        }
    }

    private static async Task<StorageFolder?> TryGetStorageFolderAsync(string rootPath, string? accessToken)
    {
        if (!string.IsNullOrWhiteSpace(accessToken))
            try
            {
                return await StorageApplicationPermissions
                    .FutureAccessList
                    .GetFolderAsync(accessToken);
            }
            catch
            {
                // Ignore missing/invalid access tokens.
            }

        try
        {
            return await StorageFolder.GetFolderFromPathAsync(rootPath);
        }
        catch
        {
            return null;
        }
    }

    private static async IAsyncEnumerable<MediaItem> ScanWindowsFolderAsync(StorageFolder root, string? sourceId,
        ModelMediaType indexedTypes, ExtensionLookup extensions,
        [EnumeratorCancellation] CancellationToken ct)
    {
        var queue = new Queue<StorageFolder>();
        queue.Enqueue(root);

        while (queue.Count > 0)
        {
            ct.ThrowIfCancellationRequested();
            var folder = queue.Dequeue();
            await foreach (var file in EnumerateStorageFilesAsync(folder, ct))
            {
                ct.ThrowIfCancellationRequested();
                if (!extensions.IsCandidateName(file.Name, indexedTypes))
                    continue;

                var path = file.Path;
                if (string.IsNullOrWhiteSpace(path) ||
                    !string.Equals(IOPath.GetFileName(path), file.Name, StringComparison.OrdinalIgnoreCase))
                {
                    var folderPath = folder.Path;
                    if (!string.IsNullOrWhiteSpace(folderPath))
                        path = IOPath.Combine(folderPath, file.Name);
                }

                if (string.IsNullOrWhiteSpace(path))
                {
                    LogScanEntry(path, file.Name, "Windows.StorageFolder", "Skipped: empty path",
                        ModelMediaType.None);
                    continue;
                }

                var mediaType = ResolveMediaTypeFromPath(path, file.Name, indexedTypes, extensions);
                if (mediaType == ModelMediaType.None)
                {
                    LogScanEntry(path, file.Name, "Windows.StorageFolder", "Skipped: media type filtered",
                        ModelMediaType.None);
                    continue;
                }

                var durationMs = 0L;

                // IMPORTANT: Retrieving video duration is expensive on Windows and significantly slows down indexing.
                // We defer duration probing to a background metadata step so scanning stays fast.


                var item = new MediaItem
                {
                    Path = path,
                    Name = file.Name,
                    DurationMs = durationMs,
                    DateAddedSeconds = new DateTimeOffset(file.DateCreated.UtcDateTime).ToUnixTimeSeconds(),
                    SourceId = sourceId,
                    MediaType = mediaType
                };
                LogScanEntry(path, file.Name, "Windows.StorageFolder", "Indexed", mediaType);
                yield return item;
            }

            await foreach (var subfolder in EnumerateStorageFoldersAsync(folder, ct))
                queue.Enqueue(subfolder);
        }
    }

    private static async IAsyncEnumerable<StorageFile> EnumerateStorageFilesAsync(StorageFolder folder,
        [EnumeratorCancellation] CancellationToken ct)
    {
        StorageFileQueryResult query;
        try
        {
            query = folder.CreateFileQuery();
        }
        catch
        {
            yield break;
        }

        uint index = 0;
        while (true)
        {
            ct.ThrowIfCancellationRequested();
            IReadOnlyList<StorageFile> batch;
            try
            {
                batch = await query.GetFilesAsync(index, StorageQueryPageSize);
            }
            catch
            {
                yield break;
            }

            if (batch.Count == 0)
                yield break;

            foreach (var file in batch)
            {
                ct.ThrowIfCancellationRequested();
                yield return file;
            }

            index += (uint)batch.Count;
            if (batch.Count < StorageQueryPageSize)
                yield break;
        }
    }

    private static async IAsyncEnumerable<StorageFolder> EnumerateStorageFoldersAsync(StorageFolder folder,
        [EnumeratorCancellation] CancellationToken ct)
    {
        StorageFolderQueryResult query;
        try
        {
            query = folder.CreateFolderQuery();
        }
        catch
        {
            yield break;
        }

        uint index = 0;
        while (true)
        {
            ct.ThrowIfCancellationRequested();
            IReadOnlyList<StorageFolder> batch;
            try
            {
                batch = await query.GetFoldersAsync(index, StorageQueryPageSize);
            }
            catch
            {
                yield break;
            }

            if (batch.Count == 0)
                yield break;

            foreach (var subfolder in batch)
            {
                ct.ThrowIfCancellationRequested();
                yield return subfolder;
            }

            index += (uint)batch.Count;
            if (batch.Count < StorageQueryPageSize)
                yield break;
        }
    }

    private static async IAsyncEnumerable<MediaItem> ScanWindowsFileSystemAsync(string root, string? sourceId,
        ModelMediaType indexedTypes, ExtensionLookup extensions,
        [EnumeratorCancellation] CancellationToken ct)
    {
        var fileChannel = Channel.CreateBounded<string>(new BoundedChannelOptions(512)
        {
            FullMode = BoundedChannelFullMode.Wait,
            SingleWriter = true,
            SingleReader = false
        });
        var itemChannel = Channel.CreateBounded<MediaItem>(new BoundedChannelOptions(512)
        {
            FullMode = BoundedChannelFullMode.Wait,
            SingleWriter = false,
            SingleReader = true
        });

        var producer = Task.Run(async () =>
        {
            try
            {
                foreach (var path in EnumerateMediaFilesStreamingWindows(root, indexedTypes, extensions, ct))
                {
                    ct.ThrowIfCancellationRequested();
                    await fileChannel.Writer.WriteAsync(path, ct).ConfigureAwait(false);
                }

                fileChannel.Writer.TryComplete();
            }
            catch (OperationCanceledException)
            {
                fileChannel.Writer.TryComplete();
            }
            catch (Exception ex)
            {
                ErrorLog.LogException(ex, "MediaStoreScanner.ScanWindowsFileSystemAsync", $"Root={root}");
                ScanLog.LogScan(root, null, "Windows.FileSystem", $"Error: {ex.Message}", ModelMediaType.None);
                fileChannel.Writer.TryComplete(ex);
            }
        }, ct);

        var workerCount = Math.Clamp(Environment.ProcessorCount, 2, 6);
        var workers = new Task[workerCount];

        for (var i = 0; i < workerCount; i++)
            workers[i] = Task.Run(async () =>
            {
                try
                {
                    await foreach (var path in fileChannel.Reader.ReadAllAsync(ct).ConfigureAwait(false))
                    {
                        try
                        {
                            var item =
                                await BuildMediaItemFromPathWindowsAsync(path, sourceId, indexedTypes, extensions, ct)
                                    .ConfigureAwait(false);
                            if (item != null)
                                await itemChannel.Writer.WriteAsync(item, ct).ConfigureAwait(false);
                        }
                        catch (OperationCanceledException)
                        {
                            throw;
                        }
                        catch (Exception ex)
                        {
                            ErrorLog.LogException(ex, "MediaStoreScanner.ScanWindowsFileSystemAsync", $"Path={path}");
                        }
                    }
                }
                catch (OperationCanceledException)
                {
                    // Cancellation is expected; let completion happen below.
                }
            }, ct);

        var completion = Task.WhenAll(workers).ContinueWith(task => { itemChannel.Writer.TryComplete(task.Exception); },
            TaskScheduler.Default);

        try
        {
            await foreach (var item in itemChannel.Reader.ReadAllAsync(ct).ConfigureAwait(false))
                yield return item;
        }
        finally
        {
            try
            {
                await producer.ConfigureAwait(false);
            }
            catch
            {
                // Exceptions are propagated through the channel completion.
            }

            try
            {
                await completion.ConfigureAwait(false);
            }
            catch
            {
                // Exceptions are propagated through the channel completion.
            }
        }
    }

    private static async ValueTask<MediaItem?> BuildMediaItemFromPathWindowsAsync(string path, string? sourceId,
        ModelMediaType indexedTypes, ExtensionLookup extensions, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        FileInfo? info = null;
        var name = IOPath.GetFileName(path);
        try
        {
            info = new FileInfo(path);
            name = info.Name;
        }
        catch (Exception ex)
        {
            LogScanEntry(path, name, "Windows.FileSystem", $"Warning: metadata read failed ({ex.Message})",
                ModelMediaType.None);
        }

        var mediaType = ResolveMediaTypeFromPath(path, name, indexedTypes, extensions);
        if (mediaType == ModelMediaType.None)
        {
            LogScanEntry(path, name, "Windows.FileSystem", "Skipped: media type filtered", ModelMediaType.None);
            return null;
        }

        var durationMs = 0L;
        if (mediaType == ModelMediaType.Videos)
            try
            {
                var file = await StorageFile.GetFileFromPathAsync(path);
                var props = await file.Properties.GetVideoPropertiesAsync();
                durationMs = (long)props.Duration.TotalMilliseconds;
            }
            catch
            {
                durationMs = 0;
            }

        var createdUtc = DateTimeOffset.UtcNow;
        if (info != null)
            try
            {
                createdUtc = new DateTimeOffset(info.CreationTimeUtc);
            }
            catch
            {
                createdUtc = DateTimeOffset.UtcNow;
            }

        var item = new MediaItem
        {
            Path = path,
            Name = name,
            DurationMs = durationMs,
            DateAddedSeconds = createdUtc.ToUnixTimeSeconds(),
            SourceId = sourceId,
            MediaType = mediaType
        };
        LogScanEntry(path, name, "Windows.FileSystem", "Indexed", mediaType);
        return item;
    }
#endif

#if ANDROID && !WINDOWS
    private static int CountMediaStore(ModelMediaType indexedTypes, ExtensionLookup extensions, CancellationToken ct)
    {
        var total = 0;

        if (indexedTypes.HasFlag(ModelMediaType.Videos))
            total += ScanMediaStoreVideos(null, ct).Count();

        if (indexedTypes.HasFlag(ModelMediaType.Photos) || indexedTypes.HasFlag(ModelMediaType.Graphics))
            total += ScanMediaStoreImages(null, indexedTypes, extensions, ct).Count();

        if (indexedTypes.HasFlag(ModelMediaType.Documents))
            total += ScanMediaStoreDocuments(null, extensions, ct).Count();

        return total;
    }

    private static int CountAndroidFolder(string rootPath, ModelMediaType indexedTypes, ExtensionLookup extensions,
        CancellationToken ct)
    {
        return ScanAndroidFolder(rootPath, null, indexedTypes, extensions, ct).Count();
    }

    private static IEnumerable<MediaItem> ScanMediaStore(string? sourceId, ModelMediaType indexedTypes,
        ExtensionLookup extensions, CancellationToken ct)
    {
        if (indexedTypes.HasFlag(ModelMediaType.Videos))
            foreach (var item in ScanMediaStoreVideos(sourceId, ct))
                yield return item;

        if (indexedTypes.HasFlag(ModelMediaType.Photos) || indexedTypes.HasFlag(ModelMediaType.Graphics))
            foreach (var item in ScanMediaStoreImages(sourceId, indexedTypes, extensions, ct))
                yield return item;

        if (indexedTypes.HasFlag(ModelMediaType.Documents))
            foreach (var item in ScanMediaStoreDocuments(sourceId, extensions, ct))
                yield return item;
    }

    private static IEnumerable<MediaItem> ScanMediaStoreVideos(string? sourceId, CancellationToken ct)
    {
        var ctx = Platform.AppContext;
        var resolver = ctx?.ContentResolver;
        var externalUri = MediaStore.Video.Media.ExternalContentUri;

        if (resolver == null || externalUri == null)
            yield break;

        // Use _ID to build a stable content:// Uri instead of relying on DATA (deprecated / restricted).
        string[] projection =
        {
            MediaStore.Video.Media.InterfaceConsts.Id,
            MediaStore.Video.Media.InterfaceConsts.DisplayName,
            MediaStore.Video.Media.InterfaceConsts.Duration,
            MediaStore.Video.Media.InterfaceConsts.DateAdded
        };

        ICursor? cursor = null;
        try
        {
            cursor = resolver.Query(
                externalUri,
                projection,
                null,
                null,
                $"{MediaStore.Video.Media.InterfaceConsts.DateAdded} DESC");
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"MediaStore query failed: {ex}");
            yield break;
        }

        using (cursor)
        {
            if (cursor == null)
                yield break;

            var idCol = cursor.GetColumnIndex(MediaStore.Video.Media.InterfaceConsts.Id);
            var nameCol = cursor.GetColumnIndex(MediaStore.Video.Media.InterfaceConsts.DisplayName);
            var durCol = cursor.GetColumnIndex(MediaStore.Video.Media.InterfaceConsts.Duration);
            var addCol = cursor.GetColumnIndex(MediaStore.Video.Media.InterfaceConsts.DateAdded);

            if (idCol < 0)
                yield break;

            while (true)
            {
                ct.ThrowIfCancellationRequested();

                bool moved;
                try
                {
                    moved = cursor.MoveToNext();
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Cursor iteration failed: {ex}");
                    yield break;
                }

                if (!moved)
                    yield break;

                long id;
                try
                {
                    id = cursor.GetLong(idCol);
                }
                catch
                {
                    continue;
                }

                var itemUri = ContentUris.WithAppendedId(externalUri, id);
                var path = itemUri?.ToString() ?? "";
                if (string.IsNullOrWhiteSpace(path))
                    continue;

                var name = "";
                if (nameCol >= 0 && !cursor.IsNull(nameCol))
                    try
                    {
                        name = cursor.GetString(nameCol) ?? "";
                    }
                    catch
                    {
                        name = "";
                    }

                long durationMs = 0;
                if (durCol >= 0 && !cursor.IsNull(durCol))
                    try
                    {
                        durationMs = cursor.GetLong(durCol);
                    }
                    catch
                    {
                        durationMs = 0;
                    }

                long addedSeconds = 0;
                if (addCol >= 0 && !cursor.IsNull(addCol))
                    try
                    {
                        addedSeconds = cursor.GetLong(addCol);
                    }
                    catch
                    {
                        addedSeconds = 0;
                    }

                if (string.IsNullOrWhiteSpace(name))
                    name = $"video_{id}";

                if (IsThumbnailPath(path, name))
                {
                    LogScanEntry(path, name, "Android.MediaStore.Video", "Skipped: thumbnail path",
                        ModelMediaType.None);
                    continue;
                }

                var item = new MediaItem
                {
                    Path = path, // content://media/external/video/media/<id>
                    Name = name,
                    DurationMs = durationMs,
                    DateAddedSeconds = addedSeconds,
                    SourceId = sourceId,
                    MediaType = ModelMediaType.Videos
                };
                LogScanEntry(path, name, "Android.MediaStore.Video", "Indexed", ModelMediaType.Videos);
                yield return item;
            }
        }
    }

    private static IEnumerable<MediaItem> ScanMediaStoreImages(string? sourceId, ModelMediaType indexedTypes,
        ExtensionLookup extensions, CancellationToken ct)
    {
        var ctx = Platform.AppContext;
        var resolver = ctx?.ContentResolver;
        var externalUri = MediaStore.Images.Media.ExternalContentUri;

        if (resolver == null || externalUri == null)
            yield break;

        string[] projection =
        {
            MediaStore.Images.Media.InterfaceConsts.Id,
            MediaStore.Images.Media.InterfaceConsts.DisplayName,
            MediaStore.Images.Media.InterfaceConsts.DateAdded,
            MediaStore.IMediaColumns.MimeType,
            MediaStore.IMediaColumns.Size
        };

        ICursor? cursor = null;
        try
        {
            cursor = resolver.Query(
                externalUri,
                projection,
                null,
                null,
                $"{MediaStore.Images.Media.InterfaceConsts.DateAdded} DESC");
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"MediaStore images query failed: {ex}");
            yield break;
        }

        using (cursor)
        {
            if (cursor == null)
                yield break;

            var idCol = cursor.GetColumnIndex(MediaStore.Images.Media.InterfaceConsts.Id);
            var nameCol = cursor.GetColumnIndex(MediaStore.Images.Media.InterfaceConsts.DisplayName);
            var addCol = cursor.GetColumnIndex(MediaStore.Images.Media.InterfaceConsts.DateAdded);
            var mimeCol = cursor.GetColumnIndex(MediaStore.IMediaColumns.MimeType);
            var sizeCol = cursor.GetColumnIndex(MediaStore.IMediaColumns.Size);

            if (idCol < 0)
                yield break;

            while (true)
            {
                ct.ThrowIfCancellationRequested();

                bool moved;
                try
                {
                    moved = cursor.MoveToNext();
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Image cursor iteration failed: {ex}");
                    yield break;
                }

                if (!moved)
                    yield break;

                long id;
                try
                {
                    id = cursor.GetLong(idCol);
                }
                catch
                {
                    continue;
                }

                var itemUri = ContentUris.WithAppendedId(externalUri, id);
                var path = itemUri?.ToString() ?? "";
                if (string.IsNullOrWhiteSpace(path))
                    continue;

                var name = "";
                if (nameCol >= 0 && !cursor.IsNull(nameCol))
                    try
                    {
                        name = cursor.GetString(nameCol) ?? "";
                    }
                    catch
                    {
                        name = "";
                    }

                long addedSeconds = 0;
                if (addCol >= 0 && !cursor.IsNull(addCol))
                    try
                    {
                        addedSeconds = cursor.GetLong(addCol);
                    }
                    catch
                    {
                        addedSeconds = 0;
                    }

                if (string.IsNullOrWhiteSpace(name))
                    name = $"photo_{id}";

                if (IsThumbnailPath(path, name))
                {
                    LogScanEntry(path, name, "Android.MediaStore.Image", "Skipped: thumbnail path",
                        ModelMediaType.None);
                    continue;
                }

                string? mimeType = null;
                if (mimeCol >= 0 && !cursor.IsNull(mimeCol))
                    try
                    {
                        mimeType = cursor.GetString(mimeCol);
                    }
                    catch
                    {
                        mimeType = null;
                    }

                long? sizeBytes = null;
                if (sizeCol >= 0 && !cursor.IsNull(sizeCol))
                    try
                    {
                        sizeBytes = cursor.GetLong(sizeCol);
                    }
                    catch
                    {
                        sizeBytes = null;
                    }

                var mediaType = ResolveMediaTypeFromPath(path, name, indexedTypes, extensions, mimeType, sizeBytes);
                if (mediaType == ModelMediaType.None)
                {
                    LogScanEntry(path, name, "Android.MediaStore.Image", "Skipped: media type filtered",
                        ModelMediaType.None);
                    continue;
                }

                var item = new MediaItem
                {
                    Path = path,
                    Name = name,
                    DurationMs = 0,
                    DateAddedSeconds = addedSeconds,
                    SourceId = sourceId,
                    MediaType = mediaType
                };
                LogScanEntry(path, name, "Android.MediaStore.Image", "Indexed", mediaType);
                yield return item;
            }
        }
    }

    private static IEnumerable<MediaItem> ScanMediaStoreDocuments(string? sourceId, ExtensionLookup extensions,
        CancellationToken ct)
    {
        var ctx = Platform.AppContext;
        var resolver = ctx?.ContentResolver;
        var externalUri = MediaStore.Files.GetContentUri("external");

        if (resolver == null || externalUri == null)
            yield break;

        string[] projection =
        {
            IBaseColumns.Id,
            MediaStore.IMediaColumns.DisplayName,
            MediaStore.Files.IFileColumns.MimeType,
            MediaStore.IMediaColumns.DateAdded,
            MediaStore.IMediaColumns.Size
        };

        ICursor? cursor = null;
        try
        {
            cursor = resolver.Query(
                externalUri,
                projection,
                null,
                null,
                $"{MediaStore.IMediaColumns.DateAdded} DESC");
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"MediaStore documents query failed: {ex}");
            yield break;
        }

        using (cursor)
        {
            if (cursor == null)
                yield break;

            var idCol = cursor.GetColumnIndex(IBaseColumns.Id);
            var nameCol = cursor.GetColumnIndex(MediaStore.IMediaColumns.DisplayName);
            var addCol = cursor.GetColumnIndex(MediaStore.IMediaColumns.DateAdded);
            var mimeCol = cursor.GetColumnIndex(MediaStore.Files.IFileColumns.MimeType);

            if (idCol < 0)
                yield break;

            while (true)
            {
                ct.ThrowIfCancellationRequested();

                bool moved;
                try
                {
                    moved = cursor.MoveToNext();
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Documents cursor iteration failed: {ex}");
                    yield break;
                }

                if (!moved)
                    yield break;

                long id;
                try
                {
                    id = cursor.GetLong(idCol);
                }
                catch
                {
                    continue;
                }

                var name = "";
                if (nameCol >= 0 && !cursor.IsNull(nameCol))
                    try
                    {
                        name = cursor.GetString(nameCol) ?? "";
                    }
                    catch
                    {
                        name = "";
                    }

                string? mimeType = null;
                if (mimeCol >= 0 && !cursor.IsNull(mimeCol))
                    try
                    {
                        mimeType = cursor.GetString(mimeCol);
                    }
                    catch
                    {
                        mimeType = null;
                    }

                var isDocument = GetMediaTypeFromMimeType(mimeType) == ModelMediaType.Documents;
                if (!isDocument && !extensions.IsDocumentName(name))
                {
                    LogScanEntry(name, name, "Android.MediaStore.Document", "Skipped: extension filtered",
                        ModelMediaType.None);
                    continue;
                }

                var itemUri = ContentUris.WithAppendedId(externalUri, id);
                var path = itemUri?.ToString() ?? "";
                if (string.IsNullOrWhiteSpace(path))
                {
                    LogScanEntry(path, name, "Android.MediaStore.Document", "Skipped: empty path",
                        ModelMediaType.None);
                    continue;
                }

                long addedSeconds = 0;
                if (addCol >= 0 && !cursor.IsNull(addCol))
                    try
                    {
                        addedSeconds = cursor.GetLong(addCol);
                    }
                    catch
                    {
                        addedSeconds = 0;
                    }

                if (string.IsNullOrWhiteSpace(name))
                    name = $"document_{id}";

                if (IsThumbnailPath(path, name))
                {
                    LogScanEntry(path, name, "Android.MediaStore.Document", "Skipped: thumbnail path",
                        ModelMediaType.None);
                    continue;
                }

                var item = new MediaItem
                {
                    Path = path,
                    Name = name,
                    DurationMs = 0,
                    DateAddedSeconds = addedSeconds,
                    SourceId = sourceId,
                    MediaType = ModelMediaType.Documents
                };
                LogScanEntry(path, name, "Android.MediaStore.Document", "Indexed", ModelMediaType.Documents);
                yield return item;
            }
        }
    }

    private static IEnumerable<MediaItem> ScanAndroidFolder(string rootPath, string? sourceId,
        ModelMediaType indexedTypes,
        ExtensionLookup extensions, CancellationToken ct)
    {
        if (rootPath.StartsWith("content://", StringComparison.OrdinalIgnoreCase))
            return ScanAndroidTreeUri(rootPath, sourceId, indexedTypes, extensions, ct);

        return ScanAndroidFileSystem(rootPath, sourceId, indexedTypes, extensions, ct);
    }

    private static IEnumerable<MediaItem> ScanAndroidTreeUri(string rootPath, string? sourceId,
        ModelMediaType indexedTypes, ExtensionLookup extensions, CancellationToken ct)
    {
        var ctx = Platform.AppContext;
        DocumentFile? root = null;

        try
        {
            var uri = Uri.Parse(rootPath);
            root = DocumentFile.FromTreeUri(ctx, uri);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"SAF tree uri parse/open failed: {ex}");
            root = null;
        }

        if (root == null)
            yield break;

        foreach (var item in TraverseDocumentTree(root, sourceId, indexedTypes, extensions, ct))
            yield return item;
    }

    private static IEnumerable<MediaItem> TraverseDocumentTree(DocumentFile root, string? sourceId,
        ModelMediaType indexedTypes, ExtensionLookup extensions, CancellationToken ct)
    {
        var stack = new Stack<DocumentFile>();
        stack.Push(root);

        while (stack.Count > 0)
        {
            ct.ThrowIfCancellationRequested();

            var dir = stack.Pop();

            DocumentFile[] children;
            try
            {
                children = dir.ListFiles() ?? Array.Empty<DocumentFile>();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"SAF ListFiles failed: {ex}");
                continue;
            }

            foreach (var child in children)
            {
                ct.ThrowIfCancellationRequested();

                if (child == null)
                    continue;

                bool isDir;
                try
                {
                    isDir = child.IsDirectory;
                }
                catch
                {
                    continue;
                }

                if (isDir)
                {
                    stack.Push(child);
                    continue;
                }

                string name;
                try
                {
                    name = child.Name ?? "";
                }
                catch
                {
                    continue;
                }

                if (!extensions.IsCandidateName(name, indexedTypes))
                {
                    LogScanEntry(name, name, "Android.SAF", "Skipped: extension filtered", ModelMediaType.None);
                    continue;
                }

                string? mimeType = null;
                try
                {
                    mimeType = child.Type;
                }
                catch
                {
                    mimeType = null;
                }

                long? sizeBytes = null;
                try
                {
                    sizeBytes = child.Length();
                }
                catch
                {
                    sizeBytes = null;
                }

                long lastModifiedMs = 0;
                try
                {
                    lastModifiedMs = child.LastModified();
                }
                catch
                {
                    lastModifiedMs = 0;
                }

                var added = lastModifiedMs > 0
                    ? lastModifiedMs / 1000
                    : DateTimeOffset.UtcNow.ToUnixTimeSeconds();

                var path = "";
                try
                {
                    path = child.Uri?.ToString() ?? "";
                }
                catch
                {
                    path = "";
                }

                if (string.IsNullOrWhiteSpace(path))
                {
                    LogScanEntry(path, name, "Android.SAF", "Skipped: empty path", ModelMediaType.None);
                    continue;
                }

                var mediaType = ResolveMediaTypeFromPath(path, name, indexedTypes, extensions, mimeType, sizeBytes);
                if (mediaType == ModelMediaType.None)
                {
                    LogScanEntry(path, name, "Android.SAF", "Skipped: media type filtered", ModelMediaType.None);
                    continue;
                }

                var item = new MediaItem
                {
                    Path = path,
                    Name = name,
                    DurationMs = 0,
                    DateAddedSeconds = added,
                    SourceId = sourceId,
                    MediaType = mediaType
                };
                LogScanEntry(path, name, "Android.SAF", "Indexed", mediaType);
                yield return item;
            }
        }
    }

    private static IEnumerable<MediaItem> ScanAndroidFileSystem(string rootPath, string? sourceId,
        ModelMediaType indexedTypes, ExtensionLookup extensions, CancellationToken ct)
    {
        if (!Directory.Exists(rootPath))
            yield break;

        foreach (var path in EnumerateMediaFilesStreamingAndroid(rootPath, indexedTypes, extensions, ct))
        {
            ct.ThrowIfCancellationRequested();

            if (string.IsNullOrWhiteSpace(path))
            {
                LogScanEntry(path, null, "Android.FileSystem", "Skipped: empty path", ModelMediaType.None);
                continue;
            }

            var mediaType = ResolveMediaTypeFromPath(path, null, indexedTypes, extensions);
            if (mediaType == ModelMediaType.None)
            {
                LogScanEntry(path, null, "Android.FileSystem", "Skipped: media type filtered", ModelMediaType.None);
                continue;
            }

            var item = new MediaItem
            {
                Path = path,
                Name = IOPath.GetFileName(path),
                DurationMs = 0,
                DateAddedSeconds = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
                SourceId = sourceId,
                MediaType = mediaType
            };
            LogScanEntry(path, IOPath.GetFileName(path), "Android.FileSystem", "Indexed", mediaType);
            yield return item;
        }
    }

    private static IEnumerable<string> EnumerateMediaFilesStreamingAndroid(string rootPath, ModelMediaType indexedTypes,
        ExtensionLookup extensions, CancellationToken ct)
    {
        var queue = new Queue<string>();
        queue.Enqueue(rootPath);

        while (queue.Count > 0)
        {
            ct.ThrowIfCancellationRequested();

            var dir = queue.Dequeue();

            IEnumerable<string> files;
            try
            {
                files = Directory.EnumerateFiles(dir, "*.*", SearchOption.TopDirectoryOnly);
            }
            catch
            {
                continue;
            }

            foreach (var file in files)
            {
                ct.ThrowIfCancellationRequested();

                if (SafeIsMediaFile(file, indexedTypes))
                {
                    var candidateType = GuessMediaType(file, null, extensions);
                    LogScanEntry(file, null, "Android.FileSystem", "Candidate", candidateType);
                    yield return file;
                }
            }

            bool SafeIsMediaFile(string file, ModelMediaType indexedTypes)
            {
                try
                {
                    if (!extensions.IsCandidatePath(file, indexedTypes))
                    {
                        LogScanEntry(file, null, "Android.FileSystem", "Skipped: extension filtered",
                            ModelMediaType.None);
                        return false;
                    }

                    if (IsThumbnailPath(file, null))
                    {
                        LogScanEntry(file, null, "Android.FileSystem", "Skipped: thumbnail path",
                            ModelMediaType.None);
                        return false;
                    }

                    return true;
                }
                catch
                {
                    // Ignore malformed paths/extensions.
                    return false;
                }
            }


            IEnumerable<string> subdirs;
            try
            {
                subdirs = Directory.EnumerateDirectories(dir, "*", SearchOption.TopDirectoryOnly);
            }
            catch
            {
                continue;
            }

            foreach (var sub in subdirs)
            {
                ct.ThrowIfCancellationRequested();
                queue.Enqueue(sub);
            }
        }
    }
#endif
}
