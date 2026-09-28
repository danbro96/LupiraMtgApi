using LupiraMtgApi.Recognition.Application.Pipeline;
using LupiraMtgApi.Recognition.Infrastructure.Imaging;
using LupiraMtgApi.Recognition.Infrastructure.Ocr;
using LupiraMtgApi.Recognition.Infrastructure.SetSymbol;

namespace LupiraMtgApi.Recognition.Application.Steps;

/// <summary>
/// Flips the crop 180° and re-runs OCR + pHash + symbol detection when
/// (a) FlorenceApi's per-region rotation says the text is upside-down (weighted median beyond ±135°),
/// (b) the best pre-fusion OCR aggregate is below <see cref="ScanScoringOptions.MediumMinOcrScore"/>, or
/// (c) both the type line and the collector line are empty. (b) and (c) catch upside-down cards Florence reads as
/// upright gibberish; (c) exists because that gibberish can still trigram-match a random card at 0.5–1.0, which
/// defeats (b). The flipped pass replaces the first only when it scores better.
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
        var firstScoring = ctx.ZoneScoring
            ?? throw new InvalidOperationException("RotationRetryStep requires ZoneScoreStep to have run first.");

        var firstBest = firstScoring.BestAggregateScore;
        var weakFirstPass = firstBest < _scoring.MediumMinOcrScore;
        var upsideDown = IsTextUpsideDown(ctx.Regions, ctx.RootSpan);
        var structureMissing = string.IsNullOrWhiteSpace(ctx.Zones.TypeLine) && string.IsNullOrWhiteSpace(ctx.Zones.BottomMetadata);
        ctx.RootSpan?.SetTag("rotation.first_pass_best_ocr", firstBest);
        if (!upsideDown && !weakFirstPass && !structureMissing)
        {
            ctx.RootSpan?.SetTag("rotation.skipped_reason", "upright_and_strong");
            return ctx;
        }

        var firstCoverage = ScanHelpers.ZoneCoverageScore(ctx.Zones);
        using var retrySpan = ScanTelemetry.Source.StartActivity("rotation.retry");
        retrySpan?.SetTag("rotation.first_pass_score", firstCoverage);
        retrySpan?.SetTag(
            "rotation.trigger",
            upsideDown ? "upside_down" : structureMissing ? "structure_missing" : "weak_first_pass");
        try
        {
            var altBytes = await ScanHelpers.Rotate180Async(preprocessed.Bytes, ct);

            var altPHashTask = _pHash.RunAsync(altBytes, ctx.ScanId, tryAltRotation: false);
            var altOcrTask = _ocr.ReadRegionsAsync(altBytes, preprocessed.MediaType, ct);
            var altSymbolTask = preprocessed.IsCropped
                ? _symbolDetector.DetectAsync(altBytes, ct)
                : Task.FromResult<SetSymbolMatch?>(null);

            await Task.WhenAll(altPHashTask, altOcrTask, altSymbolTask);
            var altPHash = altPHashTask.Result;
            var altRegions = altOcrTask.Result;
            var altSymbol = altSymbolTask.Result;

            var (altW, altH) = ScanHelpers.PickOcrDims(altRegions, preprocessed.Width, preprocessed.Height, retrySpan);
            var altZones = altW > 0 && altH > 0
                ? _classifier.Classify(altRegions, altW, altH, preprocessed.IsCropped)
                : CardZones.Empty;

            var altCoverage = ScanHelpers.ZoneCoverageScore(altZones);
            retrySpan?.SetTag("rotation.alt_pass_score", altCoverage);

            var retried = ctx with
            {
                PHashLatencyMs = ctx.PHashLatencyMs + altPHash.LatencyMs,
                RotationRetried = true,
            };

            // Rotation-signalled flips need coverage parity (guards a Florence rotation misread);
            // checked before the rescore, whose DB round-trips would be wasted on a pass that can't win.
            if (upsideDown && altCoverage < firstCoverage)
            {
                retrySpan?.SetTag("rotation.alt_won", false);
                return retried;
            }

            CardZoneScoringResult altScoring;
            using (var rescoreSpan = ScanTelemetry.Source.StartActivity("zone.score.rescore"))
            {
                altScoring = await _scorer.ScoreAsync(altZones, altSymbol, altPHash.Hits.Select(h => h.PrintingId), ct);
                rescoreSpan?.SetTag("zone.candidate_count", altScoring.ByPrinting.Count);
            }

            var altBest = altScoring.BestAggregateScore;
            retrySpan?.SetTag("rotation.alt_pass_best_ocr", altBest);

            // Weak-pass flips must actually match better, or an unreadable upright card would swap in the
            // flipped pass's equally junk text. Structure-missing flips win on recovered zones instead: the junk
            // first pass may have scored 1.0 against a random card, which a correct flipped pass can't beat.
            var altWins = upsideDown
                ? altBest >= firstBest
                : altBest > firstBest
                    || (structureMissing && altCoverage > firstCoverage && altBest >= _scoring.MediumMinOcrScore);
            retrySpan?.SetTag("rotation.alt_won", altWins);
            if (altWins)
            {
                return retried with
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
                };
            }

            return retried;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Rotation retry failed for scan {ScanId}; keeping first-pass results", ctx.ScanId);
            retrySpan?.SetTag("error.type", ex.GetType().Name);
            return ctx;
        }
    }

    private const double RotationConfidenceFloor = 0.4;

    private static bool IsTextUpsideDown(OcrRegions regions, System.Diagnostics.Activity? rootSpan)
    {
        if (regions.Regions.Count == 0)
        {
            // No OCR signal to disambiguate orientation; let the first pass stand.
            return false;
        }

        // Drop low-confidence regions: a misread fragment in the corner of the frame
        // can carry an arbitrary rotation that pollutes the median. Then weight by
        // bounding-box area so the card's title and rules text dominate over short
        // tokens in the bottom strip — those are usually upright even when the card
        // is rotated, because Florence per-region rotation flips with the text glyph.
        var candidates = regions.Regions
            .Where(r => r.Confidence >= RotationConfidenceFloor && r.Box.Area > 0)
            .Select(r => (Rotation: r.Rotation, Weight: r.Box.Area))
            .OrderBy(x => x.Rotation)
            .ToArray();

        if (candidates.Length == 0)
        {
            return false;
        }

        var totalWeight = candidates.Sum(x => x.Weight);
        var halfWeight = totalWeight / 2.0;
        var cumulative = 0.0;
        var weightedMedian = candidates[^1].Rotation;
        foreach (var (rotation, weight) in candidates)
        {
            cumulative += weight;
            if (cumulative >= halfWeight)
            {
                weightedMedian = rotation;
                break;
            }
        }

        rootSpan?.SetTag("rotation.median_degrees", weightedMedian);
        rootSpan?.SetTag("rotation.median_sample_count", candidates.Length);
        return Math.Abs(weightedMedian) > 135.0;
    }
}
