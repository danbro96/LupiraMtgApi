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
            var topRow = ctx.HydratedRows[0];
            var contributing = topRow.ZoneScores?.ContributingZoneCount(_scoring.HighZoneAgreementMinScore) ?? 0;
            if (contributing >= _scoring.HighZoneAgreementMinCount
                && HasConfidentContributingZone(topRow.ZoneScores, ctx.Zones))
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

    /// <summary>
    /// Requires that at least one zone the candidate scored well on (≥ <see cref="ScanScoringOptions.HighZoneAgreementMinScore"/>)
    /// also has OCR confidence ≥ <see cref="ScanScoringOptions.HighZoneConfidenceMinScore"/>. Prevents a
    /// trigram coincidence on a junk OCR read from promoting a scan to High.
    /// </summary>
    private bool HasConfidentContributingZone(PrintingZoneScores? zoneScores, CardZones zones)
    {
        if (_scoring.HighZoneConfidenceMinScore <= 0)
        {
            return true;
        }

        if (zoneScores is null)
        {
            return false;
        }

        var min = _scoring.HighZoneAgreementMinScore;
        var conf = _scoring.HighZoneConfidenceMinScore;

        return (zoneScores.NameScore >= min && zones.NameConfidence >= conf)
            || (zoneScores.TypeLineScore >= min && zones.TypeLineConfidence >= conf)
            || (zoneScores.RulesTextScore >= min && zones.RulesTextConfidence >= conf)
            || (zoneScores.PowerToughnessScore >= min && zones.PowerToughnessConfidence >= conf)
            || (zoneScores.BottomMetadataScore >= min && zones.BottomMetadataConfidence >= conf);
    }
}
