using System.Diagnostics;
using LupiraMtgApi.Recognition.Application.Pipeline;
using LupiraMtgApi.Recognition.Infrastructure.Imaging;
using LupiraMtgApi.Recognition.Infrastructure.Ocr;
using LupiraMtgApi.Recognition.Infrastructure.SetSymbol;

namespace LupiraMtgApi.Recognition.Application.Steps;

/// <summary>
/// Runs the three independent recognition signals in parallel: dual-rotation pHash
/// (art + full-card), Florence OCR regions, and set-symbol detection. Bundled into
/// one step because they share no inputs — splitting would require a ParallelStep
/// abstraction without buying anything.
///
/// Each signal has its own try/catch so a Florence outage doesn't kill pHash and
/// vice-versa; the signal that fails returns empty results, downstream fusion
/// degrades gracefully.
///
/// An upside-down card costs a full garbage OCR pass (~18 s) before <see cref="RotationRetryStep"/> flips it, so a
/// cheap full-card pHash probe of both orientations can decide first (<see cref="ScanScoringOptions.PHashOrientationMode"/>).
/// </summary>
public sealed class PrimaryRecognitionStep : IScanStep
{
    private readonly ScanPHashRunner _pHash;
    private readonly IOcrService _ocr;
    private readonly SetSymbolDetector _symbolDetector;
    private readonly ScanScoringOptions _scoring;
    private readonly ILogger<PrimaryRecognitionStep> _logger;

    public PrimaryRecognitionStep(
        ScanPHashRunner pHash,
        IOcrService ocr,
        SetSymbolDetector symbolDetector,
        IOptions<ScanScoringOptions> scoring,
        ILogger<PrimaryRecognitionStep> logger)
    {
        _pHash = pHash;
        _ocr = ocr;
        _symbolDetector = symbolDetector;
        _scoring = scoring.Value;
        _logger = logger;
    }

    public string Name => "primary_recognition";

    public async Task<ScanContext> ExecuteAsync(ScanContext ctx, CancellationToken ct)
    {
        var preprocessed = ctx.Preprocessed
            ?? throw new InvalidOperationException("PrimaryRecognitionStep requires CropStep to have run first.");

        var mode = _scoring.PHashOrientationMode;
        var probeTask = mode == PHashOrientationMode.Off ? null : ProbeOrientationAsync(preprocessed.Bytes, ctx.ScanId);
        var flip = false;
        if (mode == PHashOrientationMode.Flip)
        {
            flip = ShouldFlip(await probeTask!);
        }

        if (flip)
        {
            preprocessed = new CardCropResult
            {
                Bytes = await ScanHelpers.Rotate180Async(preprocessed.Bytes, ct),
                MediaType = preprocessed.MediaType,
                IsCropped = preprocessed.IsCropped,
                CropConfidence = preprocessed.CropConfidence,
                Width = preprocessed.Width,
                Height = preprocessed.Height,
                Rotated = preprocessed.Rotated,
            };
        }

        // First-pass pHash tries both rotations when the cropper had to rotate — the CW
        // default may be wrong, and pHash has no other recovery path (OCR has the
        // rotation retry below to cover its own case).
        var pHashTask = _pHash.RunAsync(preprocessed.Bytes, ctx.ScanId, tryAltRotation: preprocessed.Rotated);
        var ocrTask = RunOcrAsync(preprocessed.Bytes, preprocessed.MediaType, ctx.ScanId, ct);
        var symbolTask = preprocessed.IsCropped
            ? RunSymbolDetectAsync(preprocessed.Bytes, ct)
            : Task.FromResult<SetSymbolMatch?>(null);

        await Task.WhenAll(pHashTask, ocrTask, symbolTask, probeTask ?? Task.CompletedTask);

        if (probeTask is not null)
        {
            var (upright, flipped) = probeTask.Result;
            ctx.RootSpan?.SetTag("orientation.mode", mode.ToString());
            ctx.RootSpan?.SetTag("orientation.upright_best", upright == int.MaxValue ? -1 : upright);
            ctx.RootSpan?.SetTag("orientation.flipped_best", flipped == int.MaxValue ? -1 : flipped);
            ctx.RootSpan?.SetTag("orientation.would_flip", ShouldFlip(probeTask.Result));
            ctx.RootSpan?.SetTag("orientation.flipped", flip);
        }

        var pHashResult = pHashTask.Result;
        var (regions, ocrLatencyMs) = ocrTask.Result;
        var symbolMatch = symbolTask.Result;

        return ctx with
        {
            Preprocessed = preprocessed,
            ImageHash = pHashResult.Hash,
            PHashHits = pHashResult.Hits,
            PHashLatencyMs = pHashResult.LatencyMs,
            Regions = regions,
            OcrLatencyMs = ocrLatencyMs,
            SymbolMatch = symbolMatch,
        };
    }

    private bool ShouldFlip((int UprightBest, int FlippedBest) probe) =>
        PHashOrientation.ShouldFlip(
            probe.UprightBest,
            probe.FlippedBest,
            _scoring.PHashOrientationMaxDistance,
            _scoring.PHashOrientationMinMargin);

    private async Task<(int UprightBest, int FlippedBest)> ProbeOrientationAsync(byte[] imageBytes, Guid scanId)
    {
        using var span = ScanTelemetry.Source.StartActivity("orientation.probe");
        try
        {
            return await _pHash.ProbeOrientationAsync(imageBytes);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Orientation probe failed for scan {ScanId}; keeping the crop as-is", scanId);
            span?.SetTag("error.type", ex.GetType().Name);
            return (int.MaxValue, int.MaxValue);
        }
    }

    private async Task<(OcrRegions Regions, int LatencyMs)> RunOcrAsync(byte[] imageBytes, string mediaType, Guid scanId, CancellationToken ct)
    {
        using var span = ScanTelemetry.Source.StartActivity("ocr.regions");
        span?.SetTag("ocr.image_bytes", imageBytes.Length);

        var sw = Stopwatch.StartNew();
        try
        {
            var regions = await _ocr.ReadRegionsAsync(imageBytes, mediaType, ct);
            sw.Stop();
            span?.SetTag("ocr.region_count", regions.Regions.Count);
            return (regions, (int) sw.ElapsedMilliseconds);
        }
        catch (Exception ex)
        {
            sw.Stop();
            _logger.LogWarning(ex, "OCR regions call failed for scan {ScanId}; falling back to pHash-only candidates", scanId);
            span?.SetTag("error.type", ex.GetType().Name);
            ScanTelemetry.OcrFailures.Add(1, new KeyValuePair<string, object?>("error.type", ex.GetType().Name));
            return (OcrRegions.Empty, (int) sw.ElapsedMilliseconds);
        }
    }

    private async Task<SetSymbolMatch?> RunSymbolDetectAsync(byte[] bytes, CancellationToken ct)
    {
        using var span = ScanTelemetry.Source.StartActivity("symbol.detect");
        var match = await _symbolDetector.DetectAsync(bytes, ct);
        span?.SetTag("symbol.matched", match is not null);
        if (match is not null)
        {
            span?.SetTag("symbol.set_code", match.SetCode);
            span?.SetTag("symbol.hamming", match.HammingDistance);
            span?.SetTag("symbol.score", match.Score);
        }

        return match;
    }
}
