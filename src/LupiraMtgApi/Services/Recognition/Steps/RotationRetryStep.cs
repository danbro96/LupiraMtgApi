using LupiraMtgApi.Services.Imaging;
using LupiraMtgApi.Services.Ocr;
using LupiraMtgApi.Services.Recognition.Pipeline;
using LupiraMtgApi.Services.SetSymbol;

namespace LupiraMtgApi.Services.Recognition.Steps;

/// <summary>
/// When the cropper rotated 90° from a landscape original, FlorenceApi's per-region
/// rotation tells us whether the resulting portrait is right-side up. If the median
/// region rotation lands in [135°,180°]∪[-180°,-135°] the text is upside-down and the
/// CW pick was wrong — flip 180° and re-run OCR + pHash + symbol detection. The
/// rotation signal replaced the earlier "low coverage = retry" heuristic, which paid a
/// full extra OCR pass on every borderline scan; we now skip the retry entirely when
/// the text reads upright.
/// </summary>
public sealed class RotationRetryStep : IScanStep
{
    private readonly ScanPHashRunner _pHash;
    private readonly IOcrService _ocr;
    private readonly SetSymbolDetector _symbolDetector;
    private readonly CardZoneClassifier _classifier;
    private readonly CardZoneScorer _scorer;
    private readonly ILogger<RotationRetryStep> _logger;

    public RotationRetryStep(
        ScanPHashRunner pHash,
        IOcrService ocr,
        SetSymbolDetector symbolDetector,
        CardZoneClassifier classifier,
        CardZoneScorer scorer,
        ILogger<RotationRetryStep> logger)
    {
        _pHash = pHash;
        _ocr = ocr;
        _symbolDetector = symbolDetector;
        _classifier = classifier;
        _scorer = scorer;
        _logger = logger;
    }

    public string Name => "rotation.retry";

    public async Task<ScanContext> ExecuteAsync(ScanContext ctx, CancellationToken ct)
    {
        var preprocessed = ctx.Preprocessed
            ?? throw new InvalidOperationException("RotationRetryStep requires CropStep to have run first.");
        _ = ctx.ZoneScoring
            ?? throw new InvalidOperationException("RotationRetryStep requires ZoneScoreStep to have run first.");

        if (!preprocessed.Rotated)
        {
            return ctx;   // not rotated → no rotation ambiguity to resolve
        }

        if (!IsTextUpsideDown(ctx.Regions, ctx.RootSpan))
        {
            ctx.RootSpan?.SetTag("rotation.skipped_reason", "text_upright");
            return ctx;
        }

        using var retrySpan = ScanTelemetry.Source.StartActivity("rotation.retry");
        retrySpan?.SetTag("rotation.first_pass_score", ScanHelpers.ZoneCoverageScore(ctx.Zones));
        try
        {
            var altBytes = await ScanHelpers.Rotate180Async(preprocessed.Bytes, ct);

            var altPHashTask = _pHash.RunAsync(altBytes, ctx.ScanId, tryAltRotation: false);
            var altOcrTask = _ocr.ReadRegionsAsync(altBytes, preprocessed.MediaType, ct);
            var altSymbolTask = preprocessed.IsCropped
                ? _symbolDetector.DetectAsync(altBytes, preprocessed.MediaType, ct)
                : Task.FromResult<SetSymbolMatch?>(null);

            await Task.WhenAll(altPHashTask, altOcrTask, altSymbolTask);
            var altPHash = altPHashTask.Result;
            var altRegions = altOcrTask.Result;
            var altSymbol = altSymbolTask.Result;

            var altZones = preprocessed.Width > 0 && preprocessed.Height > 0
                ? _classifier.Classify(altRegions, preprocessed.Width, preprocessed.Height, preprocessed.IsCropped)
                : CardZones.Empty;

            var altCoverage = ScanHelpers.ZoneCoverageScore(altZones);
            retrySpan?.SetTag("rotation.alt_pass_score", altCoverage);

            // Always sum both passes' latencies — telemetry should reflect the true cost.
            var ocrLatencyMs = ctx.OcrLatencyMs;
            var pHashLatencyMs = ctx.PHashLatencyMs + altPHash.LatencyMs;

            // Coverage is the tie-breaker: rotation said the original was upside-down, but if
            // the flipped pass somehow extracts strictly less text we keep the original to
            // protect against a Florence rotation misread.
            if (altCoverage >= ScanHelpers.ZoneCoverageScore(ctx.Zones))
            {
                retrySpan?.SetTag("rotation.alt_won", true);

                using var rescoreSpan = ScanTelemetry.Source.StartActivity("zone.score.rescore");
                var altScoring = await _scorer.ScoreAsync(altZones, altSymbol, ct);
                rescoreSpan?.SetTag("zone.candidate_count", altScoring.ByPrinting.Count);

                return ctx with
                {
                    Preprocessed = new CardCropResult
                    {
                        Bytes = altBytes,
                        MediaType = preprocessed.MediaType,
                        IsCropped = preprocessed.IsCropped,
                        CropConfidence = preprocessed.CropConfidence,
                        Width = preprocessed.Width,
                        Height = preprocessed.Height,
                        Rotated = preprocessed.Rotated,
                    },
                    Zones = altZones,
                    Regions = altRegions,
                    SymbolMatch = altSymbol,
                    ImageHash = altPHash.Hash,
                    PHashHits = altPHash.Hits,
                    ZoneScoring = altScoring,
                    OcrLatencyMs = ocrLatencyMs,
                    PHashLatencyMs = pHashLatencyMs,
                    RotationRetried = true,
                };
            }

            retrySpan?.SetTag("rotation.alt_won", false);
            return ctx with
            {
                OcrLatencyMs = ocrLatencyMs,
                PHashLatencyMs = pHashLatencyMs,
            };
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Rotation retry failed for scan {ScanId}; keeping first-pass results", ctx.ScanId);
            retrySpan?.SetTag("error.type", ex.GetType().Name);
            return ctx;
        }
    }

    private static bool IsTextUpsideDown(OcrRegions regions, System.Diagnostics.Activity? rootSpan)
    {
        if (regions.Regions.Count == 0)
        {
            // No OCR signal to disambiguate orientation; let the first pass stand.
            return false;
        }

        var rotations = regions.Regions
            .Select(r => r.Rotation)
            .OrderBy(r => r)
            .ToArray();

        var median = rotations.Length % 2 == 1
            ? rotations[rotations.Length / 2]
            : (rotations[(rotations.Length / 2) - 1] + rotations[rotations.Length / 2]) / 2.0;

        rootSpan?.SetTag("rotation.median_degrees", median);
        return Math.Abs(median) > 135.0;
    }
}
