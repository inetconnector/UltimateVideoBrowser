using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;
using System.Diagnostics;
using System.Linq;
using Color = SixLabors.ImageSharp.Color;
using Point = SixLabors.ImageSharp.Point;

namespace UltimateVideoBrowser.Services.Faces;

/// <summary>
/// YuNet face detector wrapper.
/// IMPORTANT:
/// - The app ships the YuNet ONNX model as a MauiAsset and copies it to the cache on first use.
/// - We treat the model as a "single-session" detector (no extra post-process model file).
/// - At runtime we expect multi-head outputs (cls/obj/bbox/kps for 8/16/32).
/// </summary>
public sealed class YuNetFaceDetector : IDisposable
{
    private readonly SemaphoreSlim initLock = new(1, 1);
    private readonly ModelFileService modelFileService;

    private InferenceSession? detector;
    private string detectorInput = string.Empty;
    private int netHeight = 320;
    private int netWidth = 320;

    public YuNetFaceDetector(ModelFileService modelFileService)
    {
        this.modelFileService = modelFileService;
    }

    public bool IsLoaded => detector != null;

    public void Dispose()
    {
        detector?.Dispose();
    }

    public Task EnsureLoadedAsync(CancellationToken ct) => EnsureInitializedAsync(ct);

    // ---------------------------
    // Public tuning API
    // ---------------------------

    public sealed record YuNetTuning(
        float MinScore,
        float GeometryThreshold,
        float MinSizeFrac,
        float MinAreaFrac,
        float MinAspect,
        float MaxAspect,
        float NmsIou)
    {

        public static YuNetTuning Default => new(
            MinScore: 0.50f,
            GeometryThreshold: 0.45f,
            MinSizeFrac: 0.06f,
            MinAreaFrac: 0.0027f,
            MinAspect: 0.35f,
            MaxAspect: 2.5f,
            NmsIou: 0.45f); 
    }

    /// <summary>
    /// Builds a dynamic default tuning based on the current image size.
    /// </summary>
    public static YuNetTuning BuildAutoTuning(int imageW, int imageH)
    {
        // Comments intentionally in English.
        // These are "soft defaults" that scale with image size.
        float shortSide = MathF.Max(1f, MathF.Min(imageW, imageH));

        // On small images we must allow smaller faces.
        float minSizeFrac = shortSide < 700 ? 0.035f : 0.045f;

        // Area fraction roughly corresponds to (minSizeFrac^2) with extra tolerance.
        float minAreaFrac = MathF.Max(0.0009f, minSizeFrac * minSizeFrac * 0.75f);

        float minScore = shortSide < 700 ? 0.45f : 0.55f;

        return YuNetTuning.Default with
        {
            MinScore = minScore,
            MinSizeFrac = minSizeFrac,
            MinAreaFrac = minAreaFrac,
            GeometryThreshold = 0.28f,
            NmsIou = 0.35f
        };
    }

    /// <summary>
    /// Calibrates all thresholds from a reference image so that the result count is close to expectedFaces.
    /// This runs the model only once and then searches thresholds on the decoded candidate set.
    /// </summary>
    public async Task<YuNetTuning> CalibrateFromReferenceImageAsync(
        Image<Rgba32> referenceImage,
        int expectedFaces,
        CancellationToken ct)
    {
        await EnsureInitializedAsync(ct).ConfigureAwait(false);
        if (detector == null)
            return YuNetTuning.Default;

        // Run once and decode raw candidates (no filtering except "plausible box").
        var raw = await GetRawCandidatesAsync(referenceImage, ct).ConfigureAwait(false);

        // If model produces no candidates at all, keep defaults.
        if (raw.Count == 0)
            return BuildAutoTuning(referenceImage.Width, referenceImage.Height);

        // Grid search:
        // - choose parameters that hit expectedFaces
        // - prefer stricter parameters to reduce false positives
        var autoBase = BuildAutoTuning(referenceImage.Width, referenceImage.Height);

        float[] minScores = { 0.25f, 0.30f, 0.35f, 0.40f, 0.45f, 0.50f, 0.55f, 0.60f, 0.65f, 0.70f };
        float[] geomTh = { 0.00f, 0.10f, 0.15f, 0.20f, 0.25f, 0.30f, 0.35f, 0.40f, 0.45f };
        float[] minSizeFracs = { 0.02f, 0.025f, 0.03f, 0.035f, 0.04f, 0.045f, 0.05f, 0.06f };
        float[] nmsIous = { 0.30f, 0.35f, 0.40f, 0.45f };

        YuNetTuning best = autoBase;
        float bestCost = float.PositiveInfinity;

        foreach (var ms in minScores)
        {
            foreach (var gt in geomTh)
            {
                foreach (var sz in minSizeFracs)
                {
                    foreach (var iou in nmsIous)
                    {
                        var t = autoBase with
                        {
                            MinScore = ms,
                            GeometryThreshold = gt,
                            MinSizeFrac = sz,
                            MinAreaFrac = MathF.Max(0.0006f, sz * sz * 0.75f),
                            NmsIou = iou
                        };

                        int count = CountFacesWithTuning(raw, referenceImage.Width, referenceImage.Height, t);

                        // Primary objective: match expectedFaces.
                        float diff = MathF.Abs(count - expectedFaces);

                        // Secondary objective: prefer stricter settings (higher thresholds) to suppress junk.
                        // Note: These weights are tuned to keep "match count" most important.
                        float strictnessPenalty =
                            (1f - Clamp01(ms)) * 0.20f +
                            (1f - Clamp01(gt)) * 0.15f +
                            (1f - Clamp01(sz / 0.08f)) * 0.10f +
                            (1f - Clamp01(iou / 0.50f)) * 0.05f;

                        float cost = diff * 10f + strictnessPenalty;

                        if (cost < bestCost)
                        {
                            bestCost = cost;
                            best = t;
                            if (diff == 0f && strictnessPenalty < 0.20f)
                            {
                                // Good enough early exit.
                                // Keep deterministic but avoid wasting CPU.
                                // (No background work.)
                            }
                        }
                    }
                }
            }
        }

        Debug.WriteLine($"[YuNet] Calibrate: expected={expectedFaces}, bestCost={bestCost:0.000}, " +
                        $"MinScore={best.MinScore:0.00}, Geo={best.GeometryThreshold:0.00}, MinSizeFrac={best.MinSizeFrac:0.000}, NMS={best.NmsIou:0.00}");

        return best;
    }

    /// <summary>
    /// Main detection. If tuning is null, dynamic defaults are used.
    /// </summary>
    public async Task<IReadOnlyList<DetectedFace>> DetectFacesAsync(
        Image<Rgba32> image,
        float minScore,
        CancellationToken ct)
    {
        // Backwards compatible entry point.
        var t = BuildAutoTuning(image.Width, image.Height) with { MinScore = minScore };
        return await DetectFacesAsync(image, t, ct).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<DetectedFace>> DetectFacesAsync(
        Image<Rgba32> image,
        YuNetTuning? tuning,
        CancellationToken ct)
    {
        await EnsureInitializedAsync(ct).ConfigureAwait(false);
        if (detector == null)
            return Array.Empty<DetectedFace>();

        var t = tuning ?? BuildAutoTuning(image.Width, image.Height);

        var raw = await GetRawCandidatesAsync(image, ct).ConfigureAwait(false);
        if (raw.Count == 0)
            return Array.Empty<DetectedFace>();

        // Apply tuning and NMS.
        var filtered = FilterCandidates(raw, image.Width, image.Height, t);
        var final = NonMaxSuppression(filtered, t.NmsIou);

        return final;
    }

    // ---------------------------
    // Internals: raw candidates
    // ---------------------------

    private sealed class MultiStrideHeads
    {
        public int[] Strides = Array.Empty<int>();
        public Dictionary<int, Tensor<float>> Cls = new();
        public Dictionary<int, Tensor<float>> Obj = new();
        public Dictionary<int, Tensor<float>> BBox = new();
        public Dictionary<int, Tensor<float>> Kps = new();
    }

    private readonly struct RawCandidate
    {
        public readonly float X;
        public readonly float Y;
        public readonly float W;
        public readonly float H;
        public readonly float Score;
        public readonly float[] Lm;
        public readonly float Geometry;

        public RawCandidate(float x, float y, float w, float h, float score, float[] lm, float geometry)
        {
            X = x; Y = y; W = w; H = h;
            Score = score;
            Lm = lm;
            Geometry = geometry;
        }
    }

    private async Task<List<RawCandidate>> GetRawCandidatesAsync(Image<Rgba32> image, CancellationToken ct)
    {
        // Comments intentionally in English.
        await EnsureInitializedAsync(ct).ConfigureAwait(false);
        if (detector == null)
            return new List<RawCandidate>();

        Letterbox(image, netWidth, netHeight, out var boxed, out var scale, out var dx, out var dy);

        try
        {
            var input = ToFloatTensorBgr(boxed, netWidth, netHeight);

            using var output = detector.Run(new[]
            {
                NamedOnnxValue.CreateFromTensor(detectorInput, input)
            });

            // Optional: keep while tuning.
            DumpOutputShapes(output);

            var heads = CollectMultiStrideHeads(output);

            var raw = new List<RawCandidate>(256);

            foreach (var s in heads.Strides)
            {
                DecodeStrideToRaw(
                    stride: s,
                    cls: heads.Cls[s],
                    obj: heads.Obj[s],
                    bbox: heads.BBox[s],
                    kps: heads.Kps[s],
                    scale: scale,
                    dx: dx,
                    dy: dy,
                    imageW: image.Width,
                    imageH: image.Height,
                    inputW: netWidth,
                    inputH: netHeight,
                    raw: raw);
            }

            return raw;
        }
        finally
        {
            boxed.Dispose();
        }
    }

    private static void DecodeStrideToRaw(
        int stride,
        Tensor<float> cls,
        Tensor<float> obj,
        Tensor<float> bbox,
        Tensor<float> kps,
        float scale,
        float dx,
        float dy,
        int imageW,
        int imageH,
        int inputW,
        int inputH,
        List<RawCandidate> raw)
    {
        var (_, gw, count) = InferGrid(cls, stride, inputW, inputH);

        var clsArr = FlattenToCount(cls, count);
        var objArr = FlattenToCount(obj, count);

        var bboxArr = FlattenToCountByCols(bbox, count, 4);
        var kpsArr = FlattenToCountByCols(kps, count, 10);

        var candBuf = new Candidate[4];

        for (int idx = 0; idx < count; idx++)
        {
            // IMPORTANT: No sqrt, keep native probability product.
            float score = Sigmoid(clsArr[idx]) * Sigmoid(objArr[idx]);

            int gy = idx / gw;
            int gx = idx - gy * gw;
            float gridCx = (gx + 0.5f) * stride;
            float gridCy = (gy + 0.5f) * stride;

            float b0 = bboxArr[idx * 4 + 0];
            float b1 = bboxArr[idx * 4 + 1];
            float b2 = bboxArr[idx * 4 + 2];
            float b3 = bboxArr[idx * 4 + 3];

            if (!TryDecodeBestByGeometry(
                gridCx, gridCy,
                b0, b1, b2, b3,
                kpsArr, idx, stride,
                inputW, inputH,
                candBuf,
                out var ax, out var ay, out var aw, out var ah,
                out var alm, out var geom))
            {
                continue;
            }

            // Map from letterboxed input back to original image space.
            float x0 = (ax - dx) / scale;
            float y0 = (ay - dy) / scale;
            float w0 = aw / scale;
            float h0 = ah / scale;

            for (int k = 0; k < 10; k += 2)
            {
                alm[k] = (alm[k] - dx) / scale;
                alm[k + 1] = (alm[k + 1] - dy) / scale;
            }

            x0 = MathF.Max(0, MathF.Min(x0, imageW - 1));
            y0 = MathF.Max(0, MathF.Min(y0, imageH - 1));
            w0 = MathF.Max(0, MathF.Min(w0, imageW - x0));
            h0 = MathF.Max(0, MathF.Min(h0, imageH - y0));

            // Keep raw; final filtering happens later using tuning.
            raw.Add(new RawCandidate(x0, y0, w0, h0, score, alm, geom));
        }
    }

    // ---------------------------
    // Candidate decode selection by geometry (order-invariant)
    // ---------------------------

    private enum DecodeKind
    {
        XYWH_Abs,
        LTRB_FromCenter,
        LTRB_FromCenterScaled,
        DeltaExp
    }

    private readonly struct Candidate
    {
        public readonly float X;
        public readonly float Y;
        public readonly float W;
        public readonly float H;
        public readonly float[] Lm;
        public readonly DecodeKind Kind;

        public Candidate(float x, float y, float w, float h, float[] lm, DecodeKind kind)
        {
            X = x; Y = y; W = w; H = h;
            Lm = lm;
            Kind = kind;
        }
    }

    private static bool TryDecodeBestByGeometry(
        float gridCx,
        float gridCy,
        float b0, float b1, float b2, float b3,
        float[] kpsArr,
        int idx,
        int stride,
        int inputW,
        int inputH,
        Candidate[] candBuf,
        out float x,
        out float y,
        out float w,
        out float h,
        out float[] lm,
        out float geometryScore)
    {
        lm = Array.Empty<float>();
        x = y = w = h = 0f;
        geometryScore = 0f;

        int n = 0;

        if (Decode_XYWH_Abs(gridCx, gridCy, b0, b1, b2, b3, kpsArr, idx, out var x1, out var y1, out var w1, out var h1, out var lm1))
            candBuf[n++] = new Candidate(x1, y1, w1, h1, lm1, DecodeKind.XYWH_Abs);

        if (Decode_LTRB_FromCenter(gridCx, gridCy, b0, b1, b2, b3, kpsArr, idx, out var x2, out var y2, out var w2, out var h2, out var lm2))
            candBuf[n++] = new Candidate(x2, y2, w2, h2, lm2, DecodeKind.LTRB_FromCenter);

        if (Decode_LTRB_FromCenterScaled(gridCx, gridCy, b0, b1, b2, b3, kpsArr, idx, stride, out var x3, out var y3, out var w3, out var h3, out var lm3))
            candBuf[n++] = new Candidate(x3, y3, w3, h3, lm3, DecodeKind.LTRB_FromCenterScaled);

        if (Decode_DeltaExp(gridCx, gridCy, b0, b1, b2, b3, kpsArr, idx, stride, out var x4, out var y4, out var w4, out var h4, out var lm4))
            candBuf[n++] = new Candidate(x4, y4, w4, h4, lm4, DecodeKind.DeltaExp);

        float best = float.NegativeInfinity;
        int bestIdx = -1;

        for (int i = 0; i < n; i++)
        {
            var c = candBuf[i];
            if (!IsPlausibleBox(c.X, c.Y, c.W, c.H, inputW, inputH))
                continue;

            float g = FaceGeometryScoreOrderInvariant(c.X, c.Y, c.W, c.H, c.Lm);
            if (g > best)
            {
                best = g;
                bestIdx = i;
            }
        }

        if (bestIdx < 0)
            return false;

        var pick = candBuf[bestIdx];
        x = pick.X;
        y = pick.Y;
        w = pick.W;
        h = pick.H;
        lm = pick.Lm;
        geometryScore = best;
        return true;
    }

    private static float FaceGeometryScoreOrderInvariant(float x, float y, float w, float h, float[] lm)
    {
        // Returns 0..1, higher is more face-like.
        // Robust to left/right order for eyes and mouth.

        if (lm == null || lm.Length != 10)
            return 0f;

        if (w <= 0 || h <= 0)
            return 0f;

        float lex = lm[0], ley = lm[1];
        float rex = lm[2], rey = lm[3];
        float nx = lm[4], ny = lm[5];
        float lmx = lm[6], lmy = lm[7];
        float rmx = lm[8], rmy = lm[9];

        // Swap if needed.
        if (lex > rex)
        {
            (lex, rex) = (rex, lex);
            (ley, rey) = (rey, ley);
        }
        if (lmx > rmx)
        {
            (lmx, rmx) = (rmx, lmx);
            (lmy, rmy) = (rmy, lmy);
        }

        // Landmarks mostly inside (tolerant).
        float tolX = w * 0.25f;
        float tolY = h * 0.25f;

        int inside = 0;
        for (int i = 0; i < 10; i += 2)
        {
            float px = lm[i];
            float py = lm[i + 1];
            if (px >= x - tolX && px <= x + w + tolX && py >= y - tolY && py <= y + h + tolY)
                inside++;
        }
        if (inside < 3)
            return 0f;

        float eyeY = (ley + rey) * 0.5f;
        float mouthY = (lmy + rmy) * 0.5f;

        // Nose vertical placement (tolerant).
        if (!(ny > eyeY - h * 0.08f && mouthY > ny - h * 0.08f))
            return 0f;

        // Eye tilt.
        float eyeTilt = MathF.Abs(ley - rey) / MathF.Max(1f, w);
        if (eyeTilt > 0.30f)
            return 0f;

        float eyeDist = MathF.Abs(rex - lex);
        float eyeDistNorm = eyeDist / MathF.Max(1f, w);
        if (eyeDistNorm < 0.10f || eyeDistNorm > 0.90f)
            return 0f;

        // Nose near between eyes.
        if (nx < lex - eyeDist * 0.50f || nx > rex + eyeDist * 0.50f)
            return 0f;

        float mouthW = MathF.Abs(rmx - lmx);
        float mouthRel = mouthW / MathF.Max(1f, eyeDist);
        if (mouthRel < 0.20f || mouthRel > 2.40f)
            return 0f;

        // Vertical proportions.
        float eyesPos = (eyeY - y) / MathF.Max(1f, h);
        float mouthPos = (mouthY - y) / MathF.Max(1f, h);
        if (eyesPos < 0.03f || eyesPos > 0.75f)
            return 0f;
        if (mouthPos < 0.20f || mouthPos > 0.99f)
            return 0f;

        // Smooth score: prefer centered landmarks.
        float score = 1f;

        float cx = x + w * 0.5f;
        float cy = y + h * 0.5f;

        float lmCx = (lex + rex + nx + lmx + rmx) / 5f;
        float lmCy = (ley + rey + ny + lmy + rmy) / 5f;

        float offX = MathF.Abs(lmCx - cx) / MathF.Max(1f, w);
        float offY = MathF.Abs(lmCy - cy) / MathF.Max(1f, h);

        score *= MathF.Max(0.0f, 1f - (offX * 1.0f + offY * 1.0f));

        return Clamp01(score);
    }

    // ---------------------------
    // Apply tuning
    // ---------------------------

    private static int CountFacesWithTuning(List<RawCandidate> raw, int imageW, int imageH, YuNetTuning t)
    {
        var filtered = FilterCandidates(raw, imageW, imageH, t);
        var final = NonMaxSuppression(filtered, t.NmsIou);
        return final.Count;
    }

    private static List<DetectedFace> FilterCandidates(List<RawCandidate> raw, int imageW, int imageH, YuNetTuning t)
    {
        float shortSide = MathF.Max(1f, MathF.Min(imageW, imageH));
        float minSizePx = MathF.Max(18f, shortSide * t.MinSizeFrac);
        float minAreaPx = MathF.Max(18f * 18f, (imageW * (float)imageH) * t.MinAreaFrac);

        var list = new List<DetectedFace>(raw.Count);

        foreach (var c in raw)
        {
            if (c.Score < t.MinScore)
                continue;

            if (c.Geometry < t.GeometryThreshold)
                continue;

            if (c.W < minSizePx || c.H < minSizePx)
                continue;

            float area = c.W * c.H;
            if (area < minAreaPx)
                continue;

            float ar = c.W / MathF.Max(1f, c.H);
            if (ar < t.MinAspect || ar > t.MaxAspect)
                continue;

            list.Add(new DetectedFace(c.X, c.Y, c.W, c.H, c.Lm, c.Score));
        }

        return list;
    }

    // ---------------------------
    // Model I/O + decoding
    // ---------------------------

    private static MultiStrideHeads CollectMultiStrideHeads(IDisposableReadOnlyCollection<DisposableNamedOnnxValue> output)
    {
        Tensor<float>? cls8 = null, cls16 = null, cls32 = null;
        Tensor<float>? obj8 = null, obj16 = null, obj32 = null;
        Tensor<float>? bb8 = null, bb16 = null, bb32 = null;
        Tensor<float>? kp8 = null, kp16 = null, kp32 = null;

        foreach (var o in output)
        {
            if (o.Value is not Tensor<float> t) continue;
            var n = o.Name ?? string.Empty;

            if (n.Equals("cls_8", StringComparison.OrdinalIgnoreCase)) cls8 = t;
            else if (n.Equals("cls_16", StringComparison.OrdinalIgnoreCase)) cls16 = t;
            else if (n.Equals("cls_32", StringComparison.OrdinalIgnoreCase)) cls32 = t;

            else if (n.Equals("obj_8", StringComparison.OrdinalIgnoreCase)) obj8 = t;
            else if (n.Equals("obj_16", StringComparison.OrdinalIgnoreCase)) obj16 = t;
            else if (n.Equals("obj_32", StringComparison.OrdinalIgnoreCase)) obj32 = t;

            else if (n.Equals("bbox_8", StringComparison.OrdinalIgnoreCase)) bb8 = t;
            else if (n.Equals("bbox_16", StringComparison.OrdinalIgnoreCase)) bb16 = t;
            else if (n.Equals("bbox_32", StringComparison.OrdinalIgnoreCase)) bb32 = t;

            else if (n.Equals("kps_8", StringComparison.OrdinalIgnoreCase)) kp8 = t;
            else if (n.Equals("kps_16", StringComparison.OrdinalIgnoreCase)) kp16 = t;
            else if (n.Equals("kps_32", StringComparison.OrdinalIgnoreCase)) kp32 = t;
        }

        if (cls8 == null || cls16 == null || cls32 == null ||
            obj8 == null || obj16 == null || obj32 == null ||
            bb8 == null || bb16 == null || bb32 == null ||
            kp8 == null || kp16 == null || kp32 == null)
        {
            var names = string.Join(", ", output.Select(x => x.Name));
            throw new InvalidOperationException(
                $"YuNet multi-head outputs missing. Expected cls/obj/bbox/kps for 8/16/32. Outputs: {names}");
        }

        var h = new MultiStrideHeads
        {
            Strides = new[] { 8, 16, 32 }
        };

        h.Cls[8] = cls8; h.Cls[16] = cls16; h.Cls[32] = cls32;
        h.Obj[8] = obj8; h.Obj[16] = obj16; h.Obj[32] = obj32;
        h.BBox[8] = bb8; h.BBox[16] = bb16; h.BBox[32] = bb32;
        h.Kps[8] = kp8; h.Kps[16] = kp16; h.Kps[32] = kp32;

        return h;
    }

    private static ReadOnlySpan<float> GetTensorDataSpan(Tensor<float> t, out float[]? tempArray)
    {
        if (t is DenseTensor<float> dt)
        {
            tempArray = null;
            return dt.Buffer.Span;
        }

        tempArray = t.ToArray();
        return tempArray.AsSpan();
    }

    private static float[] FlattenToCount(Tensor<float> t, int count)
    {
        var dims = t.Dimensions.ToArray();
        var arr = new float[count];

        var span = GetTensorDataSpan(t, out var tmp);

        try
        {
            if (dims.Length == 3)
            {
                int n = Math.Min(count, dims[1]);
                for (int i = 0; i < n; i++)
                    arr[i] = span[i];
                return arr;
            }

            if (dims.Length == 4)
            {
                int n = Math.Min(count, dims[2] * dims[3]);
                for (int i = 0; i < n; i++)
                    arr[i] = span[i];
                return arr;
            }

            int m = Math.Min(count, span.Length);
            for (int i = 0; i < m; i++)
                arr[i] = span[i];

            return arr;
        }
        finally
        {
            _ = tmp;
        }
    }

    private static float[] FlattenToCountByCols(Tensor<float> t, int count, int cols)
    {
        var dims = t.Dimensions.ToArray();
        var arr = new float[count * cols];

        var span = GetTensorDataSpan(t, out var tmp);

        try
        {
            if (dims.Length == 3)
            {
                int n = Math.Min(count, dims[1]);
                int c = Math.Min(cols, dims[2]);
                for (int i = 0; i < n; i++)
                {
                    int srcBase = i * dims[2];
                    int dstBase = i * cols;
                    for (int j = 0; j < c; j++)
                        arr[dstBase + j] = span[srcBase + j];
                }
                return arr;
            }

            if (dims.Length == 4)
            {
                int c = dims[1];
                int h = dims[2];
                int w = dims[3];
                int n = Math.Min(count, h * w);
                int cc = Math.Min(cols, c);

                for (int i = 0; i < n; i++)
                {
                    int dstBase = i * cols;
                    for (int j = 0; j < cc; j++)
                        arr[dstBase + j] = span[j * (h * w) + i];
                }
                return arr;
            }

            int m = Math.Min(arr.Length, span.Length);
            for (int i = 0; i < m; i++)
                arr[i] = span[i];

            return arr;
        }
        finally
        {
            _ = tmp;
        }
    }

    private static (int gh, int gw, int count) InferGrid(Tensor<float> cls, int stride, int inputW, int inputH)
    {
        int gw = Math.Max(1, inputW / stride);
        int gh = Math.Max(1, inputH / stride);
        int count = gw * gh;

        var dims = cls.Dimensions.ToArray();
        if (dims.Length == 3 && dims[1] > 0)
            count = dims[1];

        return (gh, gw, count);
    }

    private static bool Decode_XYWH_Abs(
        float gridCx, float gridCy,
        float b0, float b1, float b2, float b3,
        float[] kpsArr, int idx,
        out float x, out float y, out float w, out float h,
        out float[] lm)
    {
        lm = new float[10];

        x = b0;
        y = b1;
        w = b2;
        h = b3;

        for (int k = 0; k < 10; k++)
            lm[k] = kpsArr[idx * 10 + k];

        return true;
    }

    private static bool Decode_LTRB_FromCenter(
        float gridCx, float gridCy,
        float b0, float b1, float b2, float b3,
        float[] kpsArr, int idx,
        out float x, out float y, out float w, out float h,
        out float[] lm)
    {
        lm = new float[10];

        float l = b0, t = b1, r = b2, b = b3;

        float x1 = gridCx - l;
        float y1 = gridCy - t;
        float x2 = gridCx + r;
        float y2 = gridCy + b;

        x = x1;
        y = y1;
        w = x2 - x1;
        h = y2 - y1;

        for (int p = 0; p < 5; p++)
        {
            float ox = kpsArr[idx * 10 + p * 2 + 0];
            float oy = kpsArr[idx * 10 + p * 2 + 1];
            lm[p * 2 + 0] = gridCx + ox;
            lm[p * 2 + 1] = gridCy + oy;
        }

        return true;
    }

    private static bool Decode_LTRB_FromCenterScaled(
        float gridCx, float gridCy,
        float b0, float b1, float b2, float b3,
        float[] kpsArr, int idx,
        int stride,
        out float x, out float y, out float w, out float h,
        out float[] lm)
    {
        lm = new float[10];

        float l = b0 * stride;
        float t = b1 * stride;
        float r = b2 * stride;
        float b = b3 * stride;

        float x1 = gridCx - l;
        float y1 = gridCy - t;
        float x2 = gridCx + r;
        float y2 = gridCy + b;

        x = x1;
        y = y1;
        w = x2 - x1;
        h = y2 - y1;

        for (int p = 0; p < 5; p++)
        {
            float ox = kpsArr[idx * 10 + p * 2 + 0] * stride;
            float oy = kpsArr[idx * 10 + p * 2 + 1] * stride;
            lm[p * 2 + 0] = gridCx + ox;
            lm[p * 2 + 1] = gridCy + oy;
        }

        return true;
    }

    private static bool Decode_DeltaExp(
        float gridCx, float gridCy,
        float b0, float b1, float b2, float b3,
        float[] kpsArr, int idx,
        int stride,
        out float x, out float y, out float w, out float h,
        out float[] lm)
    {
        lm = new float[10];

        float ddx = b0;
        float ddy = b1;
        float dw = b2;
        float dh = b3;

        float cx = gridCx + ddx * stride;
        float cy = gridCy + ddy * stride;
        float bw = MathF.Exp(dw) * stride;
        float bh = MathF.Exp(dh) * stride;

        x = cx - bw * 0.5f;
        y = cy - bh * 0.5f;
        w = bw;
        h = bh;

        for (int p = 0; p < 5; p++)
        {
            float ox = kpsArr[idx * 10 + p * 2 + 0] * stride;
            float oy = kpsArr[idx * 10 + p * 2 + 1] * stride;
            lm[p * 2 + 0] = cx + ox;
            lm[p * 2 + 1] = cy + oy;
        }

        return true;
    }

    private static bool IsPlausibleBox(float x, float y, float w, float h, int W, int H)
    {
        if (float.IsNaN(x) || float.IsNaN(y) || float.IsNaN(w) || float.IsNaN(h))
            return false;

        if (w <= 2 || h <= 2)
            return false;

        if (w > W * 1.5f || h > H * 1.5f)
            return false;

        if (x < -W || y < -H || x > W * 2 || y > H * 2)
            return false;

        return true;
    }

    private static float Sigmoid(float x)
    {
        if (x >= 0)
        {
            float z = MathF.Exp(-x);
            return 1f / (1f + z);
        }
        else
        {
            float z = MathF.Exp(x);
            return z / (1f + z);
        }
    }

    private static DenseTensor<float> ToFloatTensorBgr(Image<Rgba32> image, int w, int h)
    {
        var tensor = new DenseTensor<float>(new[] { 1, 3, h, w });
        image.ProcessPixelRows(accessor =>
        {
            for (int y = 0; y < h; y++)
            {
                var row = accessor.GetRowSpan(y);
                for (int x = 0; x < w; x++)
                {
                    var p = row[x];
                    tensor[0, 0, y, x] = p.B;
                    tensor[0, 1, y, x] = p.G;
                    tensor[0, 2, y, x] = p.R;
                }
            }
        });
        return tensor;
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

            if (detector.InputMetadata.TryGetValue(detectorInput, out var meta) && meta.Dimensions.Length >= 4)
            {
                var h = meta.Dimensions[^2];
                var w = meta.Dimensions[^1];
                if (h > 0) netHeight = h;
                if (w > 0) netWidth = w;
            }
        }
        finally
        {
            initLock.Release();
        }
    }

    private static List<DetectedFace> NonMaxSuppression(List<DetectedFace> faces, float iouThreshold)
    {
        if (faces.Count <= 1)
            return faces;

        var result = new List<DetectedFace>(faces.Count);
        var sorted = faces.OrderByDescending(f => f.Score).ToList();

        while (sorted.Count > 0)
        {
            var best = sorted[0];
            result.Add(best);
            sorted.RemoveAt(0);

            for (int i = sorted.Count - 1; i >= 0; i--)
            {
                if (IoU(best, sorted[i]) > iouThreshold)
                    sorted.RemoveAt(i);
            }
        }

        return result;
    }

    private static float IoU(DetectedFace a, DetectedFace b)
    {
        float ax2 = a.X + a.W;
        float ay2 = a.Y + a.H;
        float bx2 = b.X + b.W;
        float by2 = b.Y + b.H;

        float x1 = MathF.Max(a.X, b.X);
        float y1 = MathF.Max(a.Y, b.Y);
        float x2 = MathF.Min(ax2, bx2);
        float y2 = MathF.Min(ay2, by2);

        float iw = MathF.Max(0, x2 - x1);
        float ih = MathF.Max(0, y2 - y1);
        float inter = iw * ih;

        float union = a.W * a.H + b.W * b.H - inter;
        return union <= 0 ? 0 : inter / union;
    }

    private static void DumpOutputShapes(IDisposableReadOnlyCollection<DisposableNamedOnnxValue> output)
    {
        foreach (var o in output)
        {
            try
            {
                if (o.Value is Tensor<float> t)
                {
                    var dimsArr = t.Dimensions.ToArray();
                    Debug.WriteLine($"[YuNet] OUT {o.Name}: Tensor<float> [{string.Join(",", dimsArr)}]");
                }
                else
                {
                    Debug.WriteLine($"[YuNet] OUT {o.Name}: {o.Value?.GetType().FullName ?? "null"}");
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[YuNet] OUT {o.Name}: <shape dump failed> {ex.Message}");
            }
        }
    }

    private static void Letterbox(
        Image<Rgba32> source,
        int dstW,
        int dstH,
        out Image<Rgba32> boxed,
        out float scale,
        out float dx,
        out float dy)
    {
        scale = MathF.Min(dstW / (float)source.Width, dstH / (float)source.Height);
        var newW = Math.Max(1, (int)MathF.Round(source.Width * scale));
        var newH = Math.Max(1, (int)MathF.Round(source.Height * scale));

        dx = (dstW - newW) / 2f;
        dy = (dstH - newH) / 2f;

        var offsetX = (int)MathF.Round(dx);
        var offsetY = (int)MathF.Round(dy);

        var resized = source.Clone(ctx => ctx.Resize(newW, newH, KnownResamplers.Bicubic));
        boxed = new Image<Rgba32>(dstW, dstH, Color.Black);
        boxed.Mutate(ctx =>
        {
            ctx.DrawImage(resized, new Point(offsetX, offsetY), 1f);
        });
        resized.Dispose();
    }

    private static float Clamp01(float v) => v < 0f ? 0f : (v > 1f ? 1f : v);
}
