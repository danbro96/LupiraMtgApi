using LupiraMtgApi.Models;
using LupiraMtgApi.Models.Scans;
using LupiraMtgApi.Services.Recognition.Pipeline;
using Microsoft.Extensions.Options;

namespace LupiraMtgApi.Services.Recognition.Steps;

/// <summary>
/// Classifies the scan as Low / Medium / High based on the top candidate's combined
/// score and multi-zone agreement. Reads the source <see cref="RankedCandidate"/>
/// from <see cref="ScanContext.HydratedRows"/> (NOT just <c>TopRanked[0]</c>) to
/// stay in sync with the response candidates after HydrateStep skipped any
/// printings missing from the DB.
/// </summary>
public sealed class ConfidenceStep : IScanStep
{
    private readonly ScanScoringOptions _scoring;

    public ConfidenceStep(IOptions<ScanScoringOptions> scoring)
    {
        _scoring = scoring.Value;
    }

    public string Name => "confidence.classify";

    public Task<ScanContext> ExecuteAsync(ScanContext ctx, CancellationToken ct)
    {
        return Task.FromResult(ctx with { Confidence = Classify(ctx) });
    }

    private RecognitionConfidence Classify(ScanContext ctx)
    {
        if (ctx.Ranked.Count == 0)
        {
            return RecognitionConfidence.Low;
        }

        var best = ctx.Ranked[0];

        if (best.CombinedScore >= _scoring.HighCombined && ctx.HydratedRows.Count > 0)
        {
            var contributing = ctx.HydratedRows[0].ZoneScores?.ContributingZoneCount(_scoring.HighZoneAgreementMinScore) ?? 0;
            if (contributing >= _scoring.HighZoneAgreementMinCount)
            {
                return RecognitionConfidence.High;
            }
        }

        if (best.CombinedScore >= _scoring.MediumCombined)
        {
            return RecognitionConfidence.Medium;
        }

        return RecognitionConfidence.Low;
    }
}
