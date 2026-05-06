using LupiraMtgApi.Services.Recognition.Pipeline;

namespace LupiraMtgApi.Services.Recognition.Steps;

/// <summary>
/// Runs <see cref="CardZoneScorer.ScoreAsync"/> against the classified zones, threading
/// in the symbol-detector match for Tier-0/1 metadata boosts. Sits before
/// <see cref="RotationRetryStep"/> so the retry decision can use the score's
/// multi-zone agreement signal as a "first pass is confident" check.
/// </summary>
public sealed class ZoneScoreStep : IScanStep
{
    private readonly CardZoneScorer _scorer;

    public ZoneScoreStep(CardZoneScorer scorer)
    {
        _scorer = scorer;
    }

    public string Name => "zone.score";

    public async Task<ScanContext> ExecuteAsync(ScanContext ctx, CancellationToken ct)
    {
        using var span = ScanTelemetry.Source.StartActivity("zone.score");
        var scoring = await _scorer.ScoreAsync(ctx.Zones, ctx.SymbolMatch, ct);
        span?.SetTag("zone.candidate_count", scoring.ByPrinting.Count);
        span?.SetTag("zone.weights_total", scoring.Weights.TotalPresent);
        return ctx with { ZoneScoring = scoring };
    }
}
