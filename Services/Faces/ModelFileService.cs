using System.Collections.Concurrent;

namespace UltimateVideoBrowser.Services.Faces;

public sealed class ModelFileService
{
    public enum ModelStatus
    {
        Unknown,
        Ready,
        Downloading,
        Failed
    }

    // Note: We ship these as MauiAssets (Resources/Models/*.onnx) with LogicalName "models/<file>".
    // If they are not embedded, we fall back to downloading them from a stable mirror.
    private const string YuNetFile = "face_detection_yunet_2023mar.onnx";
    private const string SFaceFile = "face_recognition_sface_2021dec.onnx";

    // Stable mirrors (digiKam KDE distribution of the same model files).
    private const string YuNetUrl = "https://files.kde.org/digikam/models/facesengine/yunet/face_detection_yunet_2023mar.onnx";
    private const string SFaceUrl = "https://files.kde.org/digikam/models/facesengine/sface/face_recognition_sface_2021dec.onnx";

    private readonly ConcurrentDictionary<string, Task<string>> cache = new(StringComparer.OrdinalIgnoreCase);

    private readonly Dictionary<string, string?> errorByFile = new(StringComparer.OrdinalIgnoreCase)
    {
        [YuNetFile] = null,
        [SFaceFile] = null
    };

    private readonly HttpClient httpClient = new();
    private readonly object stateLock = new();

    private readonly Dictionary<string, ModelStatus> statusByFile = new(StringComparer.OrdinalIgnoreCase)
    {
        [YuNetFile] = ModelStatus.Unknown,
        [SFaceFile] = ModelStatus.Unknown
    };

    public string ModelsDirectoryPath => Path.Combine(FileSystem.CacheDirectory, "models");

    public ModelStatusSnapshot GetStatusSnapshot()
    {
        lock (stateLock)
        {
            // Update status based on local files (supports embedded assets and manual drop-in).
            RefreshLocalStatus_NoLock();
            return new ModelStatusSnapshot(
                statusByFile[YuNetFile],
                statusByFile[SFaceFile],
                ModelsDirectoryPath,
                errorByFile[YuNetFile],
                errorByFile[SFaceFile]);
        }
    }

    public bool AreAllModelsReady()
    {
        var s = GetStatusSnapshot();
        return s.YuNet == ModelStatus.Ready && s.SFace == ModelStatus.Ready;
    }

    public async Task<ModelStatusSnapshot> EnsureAllModelsAsync(CancellationToken ct)
    {
        try
        {
            await GetYuNetModelAsync(ct).ConfigureAwait(false);
            await GetSFaceModelAsync(ct).ConfigureAwait(false);
        }
        catch
        {
            // Individual failures are tracked per model.
        }

        return GetStatusSnapshot();
    }

    public Task<string> GetYuNetModelAsync(CancellationToken ct) => GetModelAsync(YuNetFile, YuNetUrl, ct);

    public Task<string> GetSFaceModelAsync(CancellationToken ct) => GetModelAsync(SFaceFile, SFaceUrl, ct);

    private Task<string> GetModelAsync(string fileName, string url, CancellationToken ct)
    {
        return cache.GetOrAdd(fileName, _ => EnsureModelAsync(fileName, url, ct));
    }

    private async Task<string> EnsureModelAsync(string fileName, string url, CancellationToken ct)
    {
        var targetPath = Path.Combine(ModelsDirectoryPath, fileName);

        lock (stateLock)
        {
            RefreshLocalStatus_NoLock();
            if (File.Exists(targetPath))
            {
                statusByFile[fileName] = ModelStatus.Ready;
                errorByFile[fileName] = null;
                return targetPath;
            }

            statusByFile[fileName] = ModelStatus.Downloading;
            errorByFile[fileName] = null;
        }

        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(targetPath)!);

            // 1) Try embedded MauiAsset first (offline-friendly).
            if (await TryCopyFromAppPackageAsync(fileName, targetPath, ct).ConfigureAwait(false))
            {
                lock (stateLock)
                {
                    statusByFile[fileName] = ModelStatus.Ready;
                    errorByFile[fileName] = null;
                }

                return targetPath;
            }

            // 2) Fallback: download from the internet.
            using var response = await httpClient.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct)
                .ConfigureAwait(false);
            response.EnsureSuccessStatusCode();

            await using var input = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
            await using var output = File.Create(targetPath);
            await input.CopyToAsync(output, ct).ConfigureAwait(false);

            lock (stateLock)
            {
                statusByFile[fileName] = ModelStatus.Ready;
                errorByFile[fileName] = null;
            }

            return targetPath;
        }
        catch (Exception ex)
        {
            lock (stateLock)
            {
                statusByFile[fileName] = File.Exists(targetPath) ? ModelStatus.Ready : ModelStatus.Failed;
                errorByFile[fileName] = ex.Message;
            }

            throw;
        }
    }

    private static async Task<bool> TryCopyFromAppPackageAsync(string fileName, string targetPath, CancellationToken ct)
    {
        try
        {
            // Note: LogicalName is "models/<fileName>".
            await using var src = await FileSystem.OpenAppPackageFileAsync($"models/{fileName}").ConfigureAwait(false);
            await using var dst = File.Create(targetPath);
            await src.CopyToAsync(dst, ct).ConfigureAwait(false);
            return true;
        }
        catch
        {
            // If the asset is not embedded, OpenAppPackageFileAsync throws.
            return false;
        }
    }

    private void RefreshLocalStatus_NoLock()
    {
        foreach (var file in statusByFile.Keys.ToList())
        {
            var path = Path.Combine(ModelsDirectoryPath, file);
            if (File.Exists(path))
            {
                statusByFile[file] = ModelStatus.Ready;
                if (errorByFile[file] != null)
                    errorByFile[file] = null;
            }
            else
            {
                if (statusByFile[file] == ModelStatus.Ready)
                    statusByFile[file] = ModelStatus.Unknown;
            }
        }
    }

    public sealed record ModelStatusSnapshot(
        ModelStatus YuNet,
        ModelStatus SFace,
        string ModelsDirectory,
        string? YuNetError,
        string? SFaceError);
}
