using LupiraMtgApi.Recognition.Application.Pipeline;
using LupiraMtgApi.Recognition.Infrastructure.Imaging;

namespace LupiraMtgApi.Recognition.Application.Steps;

/// <summary>
/// Final tail step. Tags the root scan span with the recognition outcome, emits the
/// greppable LogInformation summary line, dumps verbose zone detail at LogDebug, and
/// records all per-scan metrics (scan duration, candidate counts, confidence counter,
/// rotation-retry counter). Must run AFTER ConfidenceStep so the outcome is final,
/// and AFTER HydrateStep so the top candidate is known.
/// </summary>
public sealed class RecordOutcomeStep : IScanStep
{
    private readonly PHashIndex _pHashIndex;
    private readonly ILogger<RecordOutcomeStep> _logger;

    public RecordOutcomeStep(PHashIndex pHashIndex, ILogger<RecordOutcomeStep> logger)
    {
        _pHashIndex = pHashIndex;
        _logger = logger;
    }

    public string Name => "record_outcome";

    public Task<ScanContext> ExecuteAsync(ScanContext ctx, CancellationToken ct)
    {
        var preprocessed = ctx.Preprocessed
            ?? throw new InvalidOperationException("RecordOutcomeStep requires CropStep to have run first.");

        var topCandidate = ctx.Ranked.FirstOrDefault();
        var topRow = ctx.HydratedRows.FirstOrDefault();

        var rootSpan = ctx.RootSpan;
        rootSpan?.SetTag("scan.confidence", ctx.Confidence.ToString());
        rootSpan?.SetTag("scan.crop.cropped", preprocessed.IsCropped);
        rootSpan?.SetTag("scan.crop.confidence", preprocessed.CropConfidence);
        rootSpan?.SetTag("scan.crop.rotated", preprocessed.Rotated);
        rootSpan?.SetTag("scan.rotation.retried", ctx.RotationRetried);
        rootSpan?.SetTag("scan.ocr.region_count", ctx.Regions.Regions.Count);
        rootSpan?.SetTag("scan.ocr.candidate_count", ctx.ZoneScoring?.ByPrinting.Count ?? 0);
        rootSpan?.SetTag("scan.phash.candidate_count", ctx.PHashHits.Count);
        rootSpan?.SetTag("scan.phash.has_index", _pHashIndex.IsLoaded);
        rootSpan?.SetTag("scan.symbol.matched", ctx.SymbolMatch is not null);
        rootSpan?.SetTag("scan.symbol.set_code", ctx.SymbolMatch?.SetCode);
        rootSpan?.SetTag("scan.symbol.hamming", ctx.SymbolMatch?.HammingDistance);
        if (topCandidate is not null)
        {
            rootSpan?.SetTag("scan.top.printing_id", topCandidate.Printing.Id);
            rootSpan?.SetTag("scan.top.set_code", topCandidate.Printing.SetCode);
            rootSpan?.SetTag("scan.top.combined", topCandidate.CombinedScore);
            rootSpan?.SetTag("scan.top.ocr_aggregate", topCandidate.OcrAggregateScore);
            rootSpan?.SetTag("scan.top.phash", topCandidate.HammingScore);
            rootSpan?.SetTag("scan.top.set_type_weight", topCandidate.SetTypeWeight);
            rootSpan?.SetTag("scan.top.set_type", topRow?.SetType);
        }

        // Greppable summary — trace_id auto-correlated by the OTel logger pipeline.
        _logger.LogInformation(
            "Scan {ScanId} -> {Confidence} top={TopName} set={TopSetCode} combined={Combined:F3} ocr={OcrAgg:F3} pHash={PHashScore:F3} cropRotated={Rotated} retried={Retried} ocrRegions={Regions} pHashCandidates={PHashCandidates}",
            ctx.ScanId,
            ctx.Confidence,
            topCandidate?.Printing.Name ?? "(none)",
            topCandidate?.Printing.SetCode ?? "-",
            topCandidate?.CombinedScore ?? 0.0,
            topCandidate?.OcrAggregateScore ?? 0.0,
            topCandidate?.HammingScore ?? 0.0,
            preprocessed.Rotated,
            ctx.RotationRetried,
            ctx.Regions.Regions.Count,
            ctx.PHashHits.Count);

        // Verbose detail at Debug only — production stays Information.
        if (_logger.IsEnabled(LogLevel.Debug))
        {
            _logger.LogDebug(
                "Scan {ScanId} zones name={Name} type={Type} pt={PT} meta={Meta} rules.len={RulesLen}",
                ctx.ScanId,
                ctx.Zones.Name,
                ctx.Zones.TypeLine,
                ctx.Zones.PowerToughness,
                ctx.Zones.BottomMetadata,
                ctx.Zones.RulesText?.Length ?? 0);
        }

        ctx.ScanStopwatch.Stop();
        var confidenceTag = new KeyValuePair<string, object?>("confidence", ctx.Confidence.ToString());
        var rotatedTag = new KeyValuePair<string, object?>("crop.rotated", preprocessed.Rotated);
        var retriedTag = new KeyValuePair<string, object?>("rotation.retried", ctx.RotationRetried);
        ScanTelemetry.ScanDuration.Record(ctx.ScanStopwatch.Elapsed.TotalMilliseconds, confidenceTag, rotatedTag, retriedTag);
        ScanTelemetry.OcrDuration.Record(ctx.OcrLatencyMs, retriedTag);
        ScanTelemetry.PhashDuration.Record(ctx.PHashLatencyMs, retriedTag);
        ScanTelemetry.OcrRegionCount.Record(ctx.Regions.Regions.Count);
        ScanTelemetry.PhashCandidateCount.Record(ctx.PHashHits.Count);
        ScanTelemetry.ZoneCoverage.Record(ScanHelpers.ZoneCoverageScore(ctx.Zones));
        if (topCandidate is not null)
        {
            ScanTelemetry.TopCombined.Record(topCandidate.CombinedScore, confidenceTag);
        }

        ScanTelemetry.ConfidenceCount.Add(1, confidenceTag);
        if (ctx.RotationRetried)
        {
            ScanTelemetry.RotationRetryCount.Add(1);
        }

        return Task.FromResult(ctx);
    }
}
