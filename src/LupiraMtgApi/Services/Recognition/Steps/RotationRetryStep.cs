using LupiraMtgApi.Services.Imaging;
using LupiraMtgApi.Services.Ocr;
using LupiraMtgApi.Services.Recognition.Pipeline;
using LupiraMtgApi.Services.SetSymbol;
using Microsoft.Extensions.Options;

namespace LupiraMtgApi.Services.Recognition.Steps;

/// <summary>
/// When the cropper rotated 90° CW from a landscape original AND the first pass
/// produced sparse zones, flip 180° and retry OCR + pHash + symbol detection. If the
/// alt rotation populates more zones, replace the context with the alt outputs and
/// re-score. No-op when not rotated or first pass is already confident — short-circuit
/// saves ~1s per confident scan, validated in production traces.
/// </summary>
public sealed class RotationRetryStep : IScanStep
{
    private readonly ScanPHashRunner _pHash;
    private readonly IOcrService _ocr;
    private readonly SetSymbolDetector _symbolDetector;
    private readonly CardZoneClassifier _classifier;
    private readonly CardZoneScorer _scorer;
    private readonly ScanScoringOptions _scoring;
    private readonly ILogger<RotationRetryStep> _logger;

    public RotationRetryStep(
        ScanPHashRunner pHash,
        IOcrService ocr,
        SetSymbolDetector symbolDetector,
        CardZoneClassifier classifier,
        CardZoneScorer scorer,
        IOptions<ScanScoringOptions> scoring,
        ILogger<RotationRetryStep> logger)
    {
        _pHash = pHash;
        _ocr = ocr;
        _symbolDetector = symbolDetector;
        _classifier = classifier;
        _scorer = scorer;
        _scoring = scoring.Value;
        _logger = logger;
    }

    public string Name => "rotation.retry";

    public async Task<ScanContext> ExecuteAsync(ScanContext ctx, CancellationToken ct)
    {
        var preprocessed = ctx.Preprocessed
            ?? throw new InvalidOperationException("RotationRetryStep requires CropStep to have run first.");
        var scoring = ctx.ZoneScoring
            ?? throw new InvalidOperationException("RotationRetryStep requires ZoneScoreStep to have run first.");

        if (!preprocessed.Rotated)
        {
            return ctx;   // not rotated → no rotation ambiguity to resolve
        }

        if (IsFirstPassConfident(ctx.Zones, scoring, ctx.RootSpan))
        {
            // Tag the skip reason on the root span for telemetry.
            var skipReason = ScanHelpers.ZoneCoverageScore(ctx.Zones) >= _scoring.RotationRetryHighCoverageSkipThreshold
                ? "high_coverage"
                : "strong_zone_agreement";
            ctx.RootSpan?.SetTag("rotation.skipped_reason", skipReason);
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

            if (altCoverage > ScanHelpers.ZoneCoverageScore(ctx.Zones))
            {
                retrySpan?.SetTag("rotation.alt_won", true);

                // Re-score on the winning rotation.
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

    private bool IsFirstPassConfident(CardZones zones, CardZoneScoringResult scoring, System.Diagnostics.Activity? rootSpan)
    {
        var coverage = ScanHelpers.ZoneCoverageScore(zones);
        if (coverage >= _scoring.RotationRetryHighCoverageSkipThreshold)
        {
            rootSpan?.SetTag("rotation.first_pass_confidence", "high_coverage");
            return true;
        }

        if (coverage < _scoring.RotationRetryCoverageThreshold)
        {
            return false;
        }

        var topRow = scoring.ByPrinting.Values
            .OrderByDescending(r => r.AggregateScore)
            .FirstOrDefault();
        if (topRow is not null
            && topRow.ContributingZoneCount(_scoring.RotationRetryStrongZoneScoreThreshold) >= _scoring.RotationRetryStrongZoneMinCount)
        {
            rootSpan?.SetTag("rotation.first_pass_confidence", "strong_zone_agreement");
            return true;
        }

        return false;
    }
}
