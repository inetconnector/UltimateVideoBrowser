using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;
using Color = SixLabors.ImageSharp.Color;
using Point = SixLabors.ImageSharp.Point;

namespace UltimateVideoBrowser.Services.Faces;

public sealed class YuNetFaceDetector : IDisposable
{
    private const int NetWidth = 320;
    private const int NetHeight = 320;
    private readonly SemaphoreSlim initLock = new(1, 1);

    private readonly ModelFileService modelFileService;
    private string confOutput = "";
    private InferenceSession? detector;
    private string detectorInput = "";
    private string iouOutput = "";
    private string locOutput = "";
    private string postConfInput = "";
    private string postIouInput = "";
    private string postLocInput = "";
    private string postOutput = "";
    private InferenceSession? postProcessor;

    public YuNetFaceDetector(ModelFileService modelFileService)
    {
        this.modelFileService = modelFileService;
    }

    public bool IsLoaded => detector != null && postProcessor != null;

    public void Dispose()
    {
        detector?.Dispose();
        postProcessor?.Dispose();
    }

    public Task EnsureLoadedAsync(CancellationToken ct)
    {
        return EnsureInitializedAsync(ct);
    }

    public async Task<IReadOnlyList<DetectedFace>> DetectFacesAsync(Image<Rgba32> image, float minScore,
        CancellationToken ct)
    {
        await EnsureInitializedAsync(ct).ConfigureAwait(false);
        if (detector == null || postProcessor == null)
            return Array.Empty<DetectedFace>();

        Letterbox(image, NetWidth, NetHeight, out var boxed, out var scale, out var dx, out var dy);

        var input = ToFloatTensorRgb(boxed, NetWidth, NetHeight);
        using var detectorOutput = detector.Run(new[]
        {
            NamedOnnxValue.CreateFromTensor(detectorInput, input)
        });

        var loc = detectorOutput.First(x => x.Name == locOutput).AsTensor<float>();
        var conf = detectorOutput.First(x => x.Name == confOutput).AsTensor<float>();
        var iou = detectorOutput.First(x => x.Name == iouOutput).AsTensor<float>();

        using var postOutputValues = postProcessor.Run(new[]
        {
            NamedOnnxValue.CreateFromTensor(postLocInput, loc),
            NamedOnnxValue.CreateFromTensor(postConfInput, conf),
            NamedOnnxValue.CreateFromTensor(postIouInput, iou)
        });

        var detections = postOutputValues.First(x => x.Name == postOutput).AsTensor<float>();

        var faces = ParseDetections(detections, minScore, scale, dx, dy, image.Width, image.Height);
        boxed.Dispose();
        return faces;
    }

    private async Task EnsureInitializedAsync(CancellationToken ct)
    {
        if (detector != null && postProcessor != null)
            return;

        await initLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (detector != null && postProcessor != null)
                return;

            var detectorPath = await modelFileService.GetYuNetModelAsync(ct).ConfigureAwait(false);
            var postPath = await modelFileService.GetYuNetPostModelAsync(ct).ConfigureAwait(false);

            var options = new SessionOptions
            {
                GraphOptimizationLevel = GraphOptimizationLevel.ORT_ENABLE_ALL
            };

            detector = new InferenceSession(detectorPath, options);
            postProcessor = new InferenceSession(postPath, options);

            detectorInput = detector.InputMetadata.Keys.First();
            locOutput = FindKey(detector.OutputMetadata.Keys, "loc");
            confOutput = FindKey(detector.OutputMetadata.Keys, "conf");
            iouOutput = FindKey(detector.OutputMetadata.Keys, "iou");

            postLocInput = FindKey(postProcessor.InputMetadata.Keys, "loc");
            postConfInput = FindKey(postProcessor.InputMetadata.Keys, "conf");
            postIouInput = FindKey(postProcessor.InputMetadata.Keys, "iou");
            postOutput = postProcessor.OutputMetadata.Keys.First();
        }
        finally
        {
            initLock.Release();
        }
    }

    private static List<DetectedFace> ParseDetections(Tensor<float> detections, float minScore, float scale,
        float dx, float dy, int imageWidth, int imageHeight)
    {
        var faces = new List<DetectedFace>();
        var dims = detections.Dimensions.ToArray();
        if (dims.Length < 2)
            return faces;

        var rows = dims.Length == 3 ? dims[1] : dims[0];
        var cols = dims.Length == 3 ? dims[2] : dims[1];

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

        var resized = source.Clone(ctx => ctx.Resize(newW, newH, KnownResamplers.Bicubic));
        boxed = new Image<Rgba32>(dstW, dstH, Color.Black);
        var offsetX = dx;
        var offsetY = dy;
        boxed.Mutate(ctx =>
        {
            ctx.DrawImage(resized,
                new Point((int)MathF.Round(offsetX), (int)MathF.Round(offsetY)),
                1f);
        });
        resized.Dispose();
    }

    private static string FindKey(IEnumerable<string> keys, string needle)
    {
        var key = keys.FirstOrDefault(value => value.Contains(needle, StringComparison.OrdinalIgnoreCase));
        if (key == null)
            throw new InvalidOperationException($"Missing tensor '{needle}'. Available: {string.Join(", ", keys)}");
        return key;
    }
}