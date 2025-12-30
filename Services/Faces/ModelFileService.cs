using System.Collections.Concurrent;

namespace UltimateVideoBrowser.Services.Faces;

public sealed class ModelFileService
{
    private const string YuNetUrl =
        "https://github.com/opencv/opencv_zoo/raw/main/models/face_detection_yunet/face_detection_yunet_2023mar.onnx";
    private const string YuNetPostUrl =
        "https://github.com/opencv/opencv_zoo/raw/main/models/face_detection_yunet/postproc_yunet_top50_th60_320x320.onnx";
    private const string SFaceUrl =
        "https://github.com/opencv/opencv_zoo/raw/main/models/face_recognition_sface/face_recognition_sface_2021dec.onnx";

    private readonly ConcurrentDictionary<string, Task<string>> cache = new();
    private readonly HttpClient httpClient = new();

    public Task<string> GetYuNetModelAsync(CancellationToken ct)
        => GetModelAsync("face_detection_yunet_2023mar.onnx", YuNetUrl, ct);

    public Task<string> GetYuNetPostModelAsync(CancellationToken ct)
        => GetModelAsync("postproc_yunet_top50_th60_320x320.onnx", YuNetPostUrl, ct);

    public Task<string> GetSFaceModelAsync(CancellationToken ct)
        => GetModelAsync("face_recognition_sface_2021dec.onnx", SFaceUrl, ct);

    private Task<string> GetModelAsync(string fileName, string url, CancellationToken ct)
    {
        return cache.GetOrAdd(fileName, _ => DownloadModelAsync(fileName, url, ct));
    }

    private async Task<string> DownloadModelAsync(string fileName, string url, CancellationToken ct)
    {
        var targetPath = Path.Combine(FileSystem.CacheDirectory, "models", fileName);
        if (File.Exists(targetPath))
            return targetPath;

        Directory.CreateDirectory(Path.GetDirectoryName(targetPath)!);

        using var response = await httpClient.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct);
        response.EnsureSuccessStatusCode();

        await using var input = await response.Content.ReadAsStreamAsync(ct);
        await using var output = File.Create(targetPath);
        await input.CopyToAsync(output, ct);

        return targetPath;
    }
}
