using LupiraMtgApi.Services.Recognition.Pipeline;
using Microsoft.Extensions.Options;

namespace LupiraMtgApi.Services.Recognition.Steps;

/// <summary>
/// Merges per-zone OCR scores with pHash hits into a per-printing
/// <see cref="RankedCandidate"/> dictionary, then computes the FinalScore for each
/// candidate using fusion weights from <see cref="ScanScoringOptions"/>. Re-normalizes
/// when only one signal contributes (pHash-only or OCR-only) so a single strong
/// signal can still cross confidence thresholds.
/// </summary>
public sealed class FusionStep : IScanStep
{
    private readonly ScanScoringOptions _scoring;

    public FusionStep(IOptions<ScanScoringOptions> scoring)
    {
        _scoring = scoring.Value;
    }

    public string Name => "fusion";

    public Task<ScanContext> ExecuteAsync(ScanContext ctx, CancellationToken ct)
    {
        var scoring = ctx.ZoneScoring
            ?? throw new InvalidOperationException("FusionStep requires ZoneScoreStep to have run first.");

        using var span = ScanTelemetry.Source.StartActivity("fusion");

        var byPrinting = new Dictionary<string, RankedCandidate>(StringComparer.Ordinal);
        foreach (var (id, scores) in scoring.ByPrinting)
        {
            byPrinting[id] = new RankedCandidate
            {
                PrintingId = id,
                ZoneScores = scores,
                SetTypeWeight = _scoring.DefaultSetTypeWeight,
            };
        }

        foreach (var hit in ctx.PHashHits)
        {
            if (!byPrinting.TryGetValue(hit.PrintingId, out var row))
            {
                row = new RankedCandidate { PrintingId = hit.PrintingId, SetTypeWeight = _scoring.DefaultSetTypeWeight };
                byPrinting[hit.PrintingId] = row;
            }

            row.HammingDistance = hit.Distance;
            row.HammingScore = Math.Clamp(1.0 - (hit.Distance / 64.0), 0.0, 1.0);
        }

        var ocrSignalAvailable = scoring.Weights.TotalPresent > 0;
        foreach (var row in byPrinting.Values)
        {
            var ocrScore = row.ZoneScores?.AggregateScore ?? 0.0;
            var (wp, wo) = SelectFusionWeights(row.HammingDistance.HasValue, ocrSignalAvailable);
            row.FinalScore = Math.Clamp((wp * row.HammingScore) + (wo * ocrScore), 0.0, 1.0);
        }

        span?.SetTag("fusion.candidate_count", byPrinting.Count);
        return Task.FromResult(ctx with { ByPrinting = byPrinting });
    }

    /// <summary>
    /// When only one of pHash/OCR contributes, scale that signal's weight to 1.0 so a
    /// perfect single-signal match isn't capped at <see cref="ScanScoringOptions.PHashWeight"/>
    /// or <see cref="ScanScoringOptions.OcrWeight"/>. Mirrors the per-zone re-normalization
    /// inside CardZoneScorer.
    /// </summary>
    private (double PHashWeight, double OcrWeight) SelectFusionWeights(bool hasPhash, bool hasOcr) => (hasPhash, hasOcr) switch
    {
        (true, true) => (_scoring.PHashWeight, _scoring.OcrWeight),
        (true, false) => (1.0, 0.0),
        (false, true) => (0.0, 1.0),
        _ => (0.0, 0.0),
    };
}
