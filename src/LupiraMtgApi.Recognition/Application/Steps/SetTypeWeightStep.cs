using LupiraMtgApi.Catalog.Data;
using LupiraMtgApi.Recognition.Application.Pipeline;
using Microsoft.EntityFrameworkCore;

namespace LupiraMtgApi.Recognition.Application.Steps;

/// <summary>
/// Multiplies each candidate's FinalScore by a per-set-type weight so "real" sets
/// (expansion / core / masters) outrank funny / memorabilia near-ties on the same
/// OracleId. Done before the final sort so a strongly-weighted printing can overtake
/// a weakly-weighted one inside the top-N cut. Also writes <see cref="RankedCandidate.SetCode"/>
/// and <see cref="RankedCandidate.SetType"/> for downstream telemetry/persistence.
/// </summary>
public sealed class SetTypeWeightStep : IScanStep
{
    private readonly LupiraMtgDbContext _db;
    private readonly ScanScoringOptions _scoring;

    public SetTypeWeightStep(LupiraMtgDbContext db, IOptions<ScanScoringOptions> scoring)
    {
        _db = db;
        _scoring = scoring.Value;
    }

    public string Name => "set_type_weights.load";

    public async Task<ScanContext> ExecuteAsync(ScanContext ctx, CancellationToken ct)
    {
        using var span = ScanTelemetry.Source.StartActivity("set_type_weights.load");
        var weights = await LoadWeightsAsync(ctx.ByPrinting.Keys, ct);
        span?.SetTag("set_type_weights.count", weights.Count);

        foreach (var row in ctx.ByPrinting.Values)
        {
            if (weights.TryGetValue(row.PrintingId, out var info))
            {
                row.SetCode = info.SetCode;
                row.SetType = info.SetType;
                row.SetTypeWeight = info.Weight;
            }

            row.FinalScore = Math.Clamp(row.FinalScore * row.SetTypeWeight, 0.0, 1.0);
        }

        var top = ctx.ByPrinting.Values
            .OrderByDescending(r => r.FinalScore)
            .Take(_scoring.FinalTopN)
            .ToList();

        return ctx with { TopRanked = top };
    }

    private async Task<Dictionary<string, (string SetCode, string? SetType, double Weight)>> LoadWeightsAsync(
        IReadOnlyCollection<string> printingIds,
        CancellationToken ct)
    {
        var result = new Dictionary<string, (string SetCode, string? SetType, double Weight)>(StringComparer.Ordinal);
        if (printingIds.Count == 0)
        {
            return result;
        }

        var ids = printingIds.ToList();
        var setCodeByPrinting = await _db.CardPrintings
            .AsNoTracking()
            .Where(p => ids.Contains(p.Id))
            .Select(p => new { p.Id, p.SetCode })
            .ToDictionaryAsync(x => x.Id, x => x.SetCode, StringComparer.Ordinal, ct);

        if (setCodeByPrinting.Count == 0)
        {
            return result;
        }

        var setCodes = setCodeByPrinting.Values.Distinct().ToList();
        var setTypeByCode = await _db.Sets
            .AsNoTracking()
            .Where(s => setCodes.Contains(s.Code))
            .Select(s => new { s.Code, s.SetType })
            .ToDictionaryAsync(x => x.Code, x => x.SetType, StringComparer.Ordinal, ct);

        var setTypes = setTypeByCode.Values.Distinct().ToList();
        var weightByType = setTypes.Count == 0
            ? new Dictionary<string, double>(StringComparer.Ordinal)
            : await _db.SetTypeWeights
                .AsNoTracking()
                .Where(w => setTypes.Contains(w.SetType))
                .ToDictionaryAsync(w => w.SetType, w => w.Weight, StringComparer.Ordinal, ct);

        foreach (var (id, setCode) in setCodeByPrinting)
        {
            var setType = setTypeByCode.GetValueOrDefault(setCode);
            var weight = setType is not null && weightByType.TryGetValue(setType, out var w)
                ? w
                : _scoring.DefaultSetTypeWeight;
            result[id] = (setCode, setType, weight);
        }

        return result;
    }
}
