using System.Collections.Concurrent;

namespace UltimateVideoBrowser.Services.Faces;

public sealed class ModelFileService
{
    private const string YuNetUrl =
        "https://files.kde.org/digikam/models/facesengine/yunet/face_detection_yunet_2023mar.onnx";

    // NOTE: Some YuNet pipelines require an additional post-processing ONNX model.
    // The correct file name is "th6" (NOT "th60").
    private const string YuNetPostUrl =
        "https://files.kde.org/digikam/models/facesengine/yunet/postproc_yunet_top50_th6_320x320.onnx";

    private const string SFaceUrl =
        "https://files.kde.org/digikam/models/facesengine/sface/face_recognition_sface_2021dec.onnx";

    private readonly ConcurrentDictionary<string, Task<string>> cache = new();
    private readonly HttpClient httpClient = new();

    private readonly object stateLock = new();
    private readonly Dictionary<string, ModelStatus> statusByFile = new(StringComparer.OrdinalIgnoreCase)
    {
        ["face_detection_yunet_2023mar.onnx"] = ModelStatus.Unknown,
        ["postproc_yunet_top50_th6_320x320.onnx"] = ModelStatus.Unknown,
        ["face_recognition_sface_2021dec.onnx"] = ModelStatus.Unknown
    };

    private readonly Dictionary<string, string?> errorByFile = new(StringComparer.OrdinalIgnoreCase)
    {
        ["face_detection_yunet_2023mar.onnx"] = null,
        ["postproc_yunet_top50_th6_320x320.onnx"] = null,
        ["face_recognition_sface_2021dec.onnx"] = null
    };

    public enum ModelStatus
    {
        Unknown,
        Ready,
        Downloading,
        Failed
    }

    public sealed record ModelStatusSnapshot(
        ModelStatus YuNet,
        ModelStatus YuNetPost,
        ModelStatus SFace,
        string ModelsDirectory,
        string? YuNetError,
        string? YuNetPostError,
        string? SFaceError);

    public string ModelsDirectoryPath
        => Path.Combine(FileSystem.CacheDirectory, "models");

    public ModelStatusSnapshot GetStatusSnapshot()
    {
        lock (stateLock)
        {
            // Update status based on local files (supports manual drop-in of models).
            RefreshLocalStatus_NoLock();
            return new ModelStatusSnapshot(
                statusByFile["face_detection_yunet_2023mar.onnx"],
                statusByFile["postproc_yunet_top50_th6_320x320.onnx"],
                statusByFile["face_recognition_sface_2021dec.onnx"],
                ModelsDirectoryPath,
                errorByFile["face_detection_yunet_2023mar.onnx"],
                errorByFile["postproc_yunet_top50_th6_320x320.onnx"],
                errorByFile["face_recognition_sface_2021dec.onnx"]);
        }
    }

    public bool AreAllModelsReady()
    {
        var s = GetStatusSnapshot();
        return s.YuNet == ModelStatus.Ready && s.YuNetPost == ModelStatus.Ready && s.SFace == ModelStatus.Ready;
    }

    public async Task<ModelStatusSnapshot> EnsureAllModelsAsync(CancellationToken ct)
    {
        try
        {
            await GetYuNetModelAsync(ct).ConfigureAwait(false);
            await GetYuNetPostModelAsync(ct).ConfigureAwait(false);
            await GetSFaceModelAsync(ct).ConfigureAwait(false);
        }
        catch
        {
            // Individual failures are tracked per model in DownloadModelAsync.
        }

        return GetStatusSnapshot();
    }

    public Task<string> GetYuNetModelAsync(CancellationToken ct)
    {
        return GetModelAsync("face_detection_yunet_2023mar.onnx", YuNetUrl, ct);
    }

    public Task<string> GetYuNetPostModelAsync(CancellationToken ct)
    {
        return GetModelAsync("postproc_yunet_top50_th6_320x320.onnx", YuNetPostUrl, ct);
    }

    public Task<string> GetSFaceModelAsync(CancellationToken ct)
    {
        return GetModelAsync("face_recognition_sface_2021dec.onnx", SFaceUrl, ct);
    }

    private Task<string> GetModelAsync(string fileName, string url, CancellationToken ct)
    {
        return cache.GetOrAdd(fileName, _ => DownloadModelAsync(fileName, url, ct));
    }

    private async Task<string> DownloadModelAsync(string fileName, string url, CancellationToken ct)
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

            // 1) Try embedded MauiAsset (offline-friendly).
            // If present, copy to cache and mark as ready.
            if (await TryCopyFromAppPackageAsync(fileName, targetPath, ct).ConfigureAwait(false))
            {
                lock (stateLock)
                {
                    statusByFile[fileName] = ModelStatus.Ready;
                    errorByFile[fileName] = null;
                }
                return targetPath;
            }

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
            await using var src = await FileSystem.OpenAppPackageFileAsync($"models/{fileName}").ConfigureAwait(false);
            await using var dst = File.Create(targetPath);
            await src.CopyToAsync(dst, ct).ConfigureAwait(false);
            return true;
        }
        catch
        {
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
}