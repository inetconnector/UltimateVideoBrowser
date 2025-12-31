using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace UltimateVideoBrowser.Services.Faces;

public sealed class SFaceRecognizer : IDisposable
{
    private const int OutputWidth = 112;
    private const int OutputHeight = 112;

    private static readonly (double X, double Y)[] CanonicalLandmarks =
    [
        (38.2946, 51.6963),
        (73.5318, 51.5014),
        (56.0252, 71.7366),
        (41.5493, 92.3655),
        (70.7299, 92.2041)
    ];

    private readonly SemaphoreSlim initLock = new(1, 1);

    private readonly ModelFileService modelFileService;
    private string inputName = "";
    private string outputName = "";
    private InferenceSession? session;

    public SFaceRecognizer(ModelFileService modelFileService)
    {
        this.modelFileService = modelFileService;
    }

    public bool IsLoaded => session != null;

    public void Dispose()
    {
        session?.Dispose();
    }

    public Task EnsureLoadedAsync(CancellationToken ct)
    {
        return EnsureInitializedAsync(ct);
    }

    public async Task<float[]> ExtractEmbeddingAsync(Image<Rgba32> source, DetectedFace face, CancellationToken ct)
    {
        await EnsureInitializedAsync(ct).ConfigureAwait(false);
        if (session == null)
            return Array.Empty<float>();

        var srcPoints = new (double X, double Y)[5];
        for (var i = 0; i < 5; i++)
            srcPoints[i] = (face.Landmarks10[i * 2], face.Landmarks10[i * 2 + 1]);

        var (a, b, tx, ty) = SolveSimilarityTransform(srcPoints, CanonicalLandmarks);
        using var aligned = WarpAffine(source, a, b, tx, ty, OutputWidth, OutputHeight);

        var input = ToFloatTensorRgb(aligned, OutputWidth, OutputHeight);
        using var result = session.Run(new[]
        {
            NamedOnnxValue.CreateFromTensor(inputName, input)
        });

        var embedding = result.First(x => x.Name == outputName).AsTensor<float>().ToArray();
        NormalizeL2InPlace(embedding);
        return embedding;
    }

    private async Task EnsureInitializedAsync(CancellationToken ct)
    {
        if (session != null)
            return;

        await initLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (session != null)
                return;

            var modelPath = await modelFileService.GetSFaceModelAsync(ct).ConfigureAwait(false);
            var options = new SessionOptions
            {
                GraphOptimizationLevel = GraphOptimizationLevel.ORT_ENABLE_ALL
            };

            session = new InferenceSession(modelPath, options);
            inputName = session.InputMetadata.Keys.First();
            outputName = session.OutputMetadata.Keys.First();
        }
        finally
        {
            initLock.Release();
        }
    }

    public static float CosineSimilarity(float[] a, float[] b)
    {
        if (a.Length != b.Length)
            throw new ArgumentException("Embedding size mismatch.");

        double dot = 0;
        for (var i = 0; i < a.Length; i++)
            dot += a[i] * b[i];

        return (float)dot;
    }

    private static void NormalizeL2InPlace(float[] vector)
    {
        double sum = 0;
        for (var i = 0; i < vector.Length; i++)
            sum += vector[i] * vector[i];

        var norm = Math.Sqrt(sum);
        if (norm <= 1e-12)
            return;

        var inv = 1.0 / norm;
        for (var i = 0; i < vector.Length; i++)
            vector[i] = (float)(vector[i] * inv);
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

    private static (double a, double b, double tx, double ty) SolveSimilarityTransform(
        (double X, double Y)[] src,
        (double X, double Y)[] dst)
    {
        if (src.Length != 5 || dst.Length != 5)
            throw new ArgumentException("Expected 5 landmarks.");

        var ata = new double[4, 4];
        var atb = new double[4];

        for (var i = 0; i < 5; i++)
        {
            var x = src[i].X;
            var y = src[i].Y;
            var X = dst[i].X;
            var Y = dst[i].Y;

            Accumulate(ata, atb, [x, -y, 1, 0], X);
            Accumulate(ata, atb, [y, x, 0, 1], Y);
        }

        var p = Solve4x4(ata, atb);
        return (p[0], p[1], p[2], p[3]);

        static void Accumulate(double[,] ata, double[] atb, double[] row, double rhs)
        {
            for (var r = 0; r < 4; r++)
            {
                atb[r] += row[r] * rhs;
                for (var c = 0; c < 4; c++)
                    ata[r, c] += row[r] * row[c];
            }
        }

        static double[] Solve4x4(double[,] a, double[] b)
        {
            var m = new double[4, 5];
            for (var r = 0; r < 4; r++)
            {
                for (var c = 0; c < 4; c++)
                    m[r, c] = a[r, c];
                m[r, 4] = b[r];
            }

            for (var col = 0; col < 4; col++)
            {
                var pivot = col;
                var max = Math.Abs(m[col, col]);
                for (var r = col + 1; r < 4; r++)
                {
                    var value = Math.Abs(m[r, col]);
                    if (value > max)
                    {
                        max = value;
                        pivot = r;
                    }
                }

                if (max < 1e-12)
                    throw new InvalidOperationException("Singular transform matrix.");

                if (pivot != col)
                    for (var c = col; c < 5; c++)
                        (m[col, c], m[pivot, c]) = (m[pivot, c], m[col, c]);

                var diag = m[col, col];
                for (var c = col; c < 5; c++)
                    m[col, c] /= diag;

                for (var r = 0; r < 4; r++)
                {
                    if (r == col)
                        continue;

                    var factor = m[r, col];
                    for (var c = col; c < 5; c++)
                        m[r, c] -= factor * m[col, c];
                }
            }

            return [m[0, 4], m[1, 4], m[2, 4], m[3, 4]];
        }
    }

    private static Image<Rgba32> WarpAffine(Image<Rgba32> src, double a, double b, double tx, double ty, int dstW,
        int dstH)
    {
        var denom = a * a + b * b;
        if (denom < 1e-12)
            throw new InvalidOperationException("Invalid affine transform.");

        var inv00 = a / denom;
        var inv01 = b / denom;
        var inv10 = -b / denom;
        var inv11 = a / denom;

        var dst = new Image<Rgba32>(dstW, dstH);
        dst.ProcessPixelRows(accessor =>
        {
            for (var y = 0; y < dstH; y++)
            {
                var row = accessor.GetRowSpan(y);
                for (var x = 0; x < dstW; x++)
                {
                    var xf = x - tx;
                    var yf = y - ty;
                    var sx = inv00 * xf + inv01 * yf;
                    var sy = inv10 * xf + inv11 * yf;
                    row[x] = SampleBilinear(src, sx, sy);
                }
            }
        });

        return dst;
    }

    private static Rgba32 SampleBilinear(Image<Rgba32> image, double x, double y)
    {
        var x0 = (int)Math.Floor(x);
        var y0 = (int)Math.Floor(y);
        var x1 = x0 + 1;
        var y1 = y0 + 1;

        if (x0 < 0 || y0 < 0 || x1 >= image.Width || y1 >= image.Height)
            return new Rgba32(0, 0, 0, 255);

        var dx = x - x0;
        var dy = y - y0;

        var p00 = image[x0, y0];
        var p10 = image[x1, y0];
        var p01 = image[x0, y1];
        var p11 = image[x1, y1];

        byte Lerp(byte a, byte b, double t)
        {
            return (byte)Math.Clamp(a + t * (b - a), 0, 255);
        }

        var r0 = Lerp(p00.R, p10.R, dx);
        var g0 = Lerp(p00.G, p10.G, dx);
        var b0 = Lerp(p00.B, p10.B, dx);

        var r1 = Lerp(p01.R, p11.R, dx);
        var g1 = Lerp(p01.G, p11.G, dx);
        var b1 = Lerp(p01.B, p11.B, dx);

        return new Rgba32(
            Lerp(r0, r1, dy),
            Lerp(g0, g1, dy),
            Lerp(b0, b1, dy),
            255);
    }
}