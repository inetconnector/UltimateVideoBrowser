using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;
using Color = SixLabors.ImageSharp.Color;
using Point = SixLabors.ImageSharp.Point;

namespace UltimateVideoBrowser.Services.Faces;

/// <summary>
/// YuNet face detector wrapper.
///
/// IMPORTANT:
/// - The app ships the YuNet ONNX model as a MauiAsset and copies it to the cache on first use.
/// - We treat the model as a "single-session" detector (no extra post-process model file).
/// - At runtime we auto-detect which output tensor contains detections.
///
/// This keeps the distribution simple (only one detector model file).
/// </summary>
public sealed class YuNetFaceDetector : IDisposable
{
    private int netWidth = 320;
    private int netHeight = 320;

    private readonly SemaphoreSlim initLock = new(1, 1);
    private readonly ModelFileService modelFileService;

    private InferenceSession? detector;
    private string detectorInput = string.Empty;

    public YuNetFaceDetector(ModelFileService modelFileService)
    {
        this.modelFileService = modelFileService;
    }

    public bool IsLoaded => detector != null;

    public void Dispose()
    {
        detector?.Dispose();
    }

    public Task EnsureLoadedAsync(CancellationToken ct)
    {
        return EnsureInitializedAsync(ct);
    }

    public async Task<IReadOnlyList<DetectedFace>> DetectFacesAsync(Image<Rgba32> image, float minScore,
        CancellationToken ct)
    {
        await EnsureInitializedAsync(ct).ConfigureAwait(false);
        if (detector == null)
            return Array.Empty<DetectedFace>();

        Letterbox(image, netWidth, netHeight, out var boxed, out var scale, out var dx, out var dy);

        var input = ToFloatTensorRgb(boxed, netWidth, netHeight);
        using var output = detector.Run(new[]
        {
            NamedOnnxValue.CreateFromTensor(detectorInput, input)
        });

        // YuNet variants differ slightly in their output signature.
        // Prefer a 2D/3D float tensor with >= 15 columns in the last dimension.
        var detections = output
            .Select(v => (Name: v.Name, Tensor: v.AsTensor<float>()))
            .OrderByDescending(x => GuessDetectionScore(x.Tensor))
            .FirstOrDefault();

        var faces = new List<DetectedFace>();
        if (detections.Tensor != null && GuessDetectionScore(detections.Tensor) > 0)
            faces = ParseDetections(detections.Tensor, minScore, scale, dx, dy, image.Width, image.Height);

        boxed.Dispose();
        return faces;
    }

    private async Task EnsureInitializedAsync(CancellationToken ct)
    {
        if (detector != null)
            return;

        await initLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (detector != null)
                return;

            var detectorPath = await modelFileService.GetYuNetModelAsync(ct).ConfigureAwait(false);

            var options = new SessionOptions
            {
                GraphOptimizationLevel = GraphOptimizationLevel.ORT_ENABLE_ALL
            };

            detector = new InferenceSession(detectorPath, options);
            detectorInput = detector.InputMetadata.Keys.First();
            // Detect the required network input size from the model metadata.
            // YuNet models may expect 320x320 or 640x640 depending on the variant.
            if (detector.InputMetadata.TryGetValue(detectorInput, out var meta) && meta.Dimensions.Length >= 4)
            {
                var h = meta.Dimensions[^2];
                var w = meta.Dimensions[^1];
                // Some models use -1 for dynamic dimensions; keep defaults in that case.
                if (h > 0) netHeight = (int)h;
                if (w > 0) netWidth = (int)w;
            }
        }
        finally
        {
            initLock.Release();
        }
    }

    private static int GuessDetectionScore(Tensor<float> t)
    {
        var dims = t.Dimensions.ToArray();
        if (dims.Length < 2)
            return 0;

        var cols = dims[^1];
        // The standard YuNet detection row has 15 floats:
        // [x, y, w, h, l0x, l0y, l1x, l1y, l2x, l2y, l3x, l3y, l4x, l4y, score]
        return cols >= 15 ? cols : 0;
    }

    private static List<DetectedFace> ParseDetections(Tensor<float> detections, float minScore, float scale,
        float dx, float dy, int imageWidth, int imageHeight)
    {
        var faces = new List<DetectedFace>();
        var dims = detections.Dimensions.ToArray();
        if (dims.Length < 2)
            return faces;

        var rows = dims.Length == 3 ? dims[1] : dims[0];
        var cols = dims[^1];

        if (cols < 15)
            return faces;

        for (var r = 0; r < rows; r++)
        {
            var score = GetDetectionValue(detections, dims, r, 14);
            if (score < minScore)
                continue;

            var x = GetDetectionValue(detections, dims, r, 0);
            var y = GetDetectionValue(detections, dims, r, 1);
            var w = GetDetectionValue(detections, dims, r, 2);
            var h = GetDetectionValue(detections, dims, r, 3);

            var landmarks = new float[10];
            for (var i = 0; i < 10; i++)
                landmarks[i] = GetDetectionValue(detections, dims, r, 4 + i);

            // Undo letterbox
            x = (x - dx) / scale;
            y = (y - dy) / scale;
            w = w / scale;
            h = h / scale;

            for (var i = 0; i < 10; i += 2)
            {
                landmarks[i] = (landmarks[i] - dx) / scale;
                landmarks[i + 1] = (landmarks[i + 1] - dy) / scale;
            }

            x = MathF.Max(0, MathF.Min(x, imageWidth - 1));
            y = MathF.Max(0, MathF.Min(y, imageHeight - 1));
            w = MathF.Max(0, MathF.Min(w, imageWidth - x));
            h = MathF.Max(0, MathF.Min(h, imageHeight - y));

            faces.Add(new DetectedFace(x, y, w, h, landmarks, score));
        }

        return faces;
    }

    private static float GetDetectionValue(Tensor<float> detections, int[] dims, int row, int col)
    {
        return dims.Length == 3 ? detections[0, row, col] : detections[row, col];
    }

    private static DenseTensor<float> ToFloatTensorRgb(Image<Rgba32> image, int w, int h)
    {
        var tensor = new DenseTensor<float>(new[] { 1, 3, h, w });
        image.ProcessPixelRows(accessor =>
        {
            for (var y = 0; y < h; y++)
            {
                var row = accessor.GetRowSpan(y);
                for (var x = 0; x < w; x++)
                {
                    var pixel = row[x];
                    tensor[0, 0, y, x] = pixel.R;
                    tensor[0, 1, y, x] = pixel.G;
                    tensor[0, 2, y, x] = pixel.B;
                }
            }
        });
        return tensor;
    }

    private static void Letterbox(Image<Rgba32> source, int dstW, int dstH, out Image<Rgba32> boxed,
        out float scale, out float dx, out float dy)
    {
        scale = MathF.Min(dstW / (float)source.Width, dstH / (float)source.Height);
        var newW = Math.Max(1, (int)MathF.Round(source.Width * scale));
        var newH = Math.Max(1, (int)MathF.Round(source.Height * scale));

        dx = (dstW - newW) / 2f;
        dy = (dstH - newH) / 2f;

        // Avoid capturing out parameters inside a lambda (CS1628).
        var offsetX = (int)MathF.Round(dx);
        var offsetY = (int)MathF.Round(dy);

        var resized = source.Clone(ctx => ctx.Resize(newW, newH, KnownResamplers.Bicubic));
        boxed = new Image<Rgba32>(dstW, dstH, Color.Black);
        boxed.Mutate(ctx =>
        {
            ctx.DrawImage(resized,
                    new Point(offsetX, offsetY),
                1f);
        });
        resized.Dispose();
    }
}