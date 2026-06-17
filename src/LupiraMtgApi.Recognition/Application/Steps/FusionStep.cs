using LupiraMtgApi.Recognition.Application.Pipeline;

namespace LupiraMtgApi.Recognition.Application.Steps;

/// <summary>
/// Merges per-zone OCR scores with pHash hits into a per-printing
/// <see cref="RankedCandidate"/> dictionary, then computes a FinalScore by treating
/// pHash and OCR as independent likelihood signals and combining them via probabilistic
/// OR: <c>1 − (1 − ocrScore) × (1 − hammingScore)</c>. The combination is monotonic in
/// each input, so adding a corroborating signal never lowers a candidate's score —
/// fixing the prior bug where an OCR-only candidate could outrank an OCR+pHash
/// candidate of the same oracle. The formula is parameter-free; <c>PHashWeight</c> and
/// <c>OcrWeight</c> on <see cref="ScanScoringOptions"/> are unused by fusion now and
/// remain only as no-op config keys for backward compatibility.
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
        _ = ctx.ZoneScoring
            ?? throw new InvalidOperationException("FusionStep requires ZoneScoreStep to have run first.");

        using var span = ScanTelemetry.Source.StartActivity("fusion");

        var byPrinting = new Dictionary<string, RankedCandidate>(StringComparer.Ordinal);
        foreach (var (id, scores) in ctx.ZoneScoring.ByPrinting)
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

        foreach (var row in byPrinting.Values)
        {
            var ocrScore = Math.Clamp(row.ZoneScores?.AggregateScore ?? 0.0, 0.0, 1.0);
            var phashScore = Math.Clamp(row.HammingScore, 0.0, 1.0);
            row.FinalScore = 1.0 - ((1.0 - ocrScore) * (1.0 - phashScore));
        }

        span?.SetTag("fusion.candidate_count", byPrinting.Count);
        return Task.FromResult(ctx with { ByPrinting = byPrinting });
    }
}
