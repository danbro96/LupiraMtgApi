using LupiraMtgApi.Catalog.Data;
using LupiraMtgApi.Catalog.Mappers;
using LupiraMtgApi.Recognition.Application.Pipeline;
using LupiraMtgApi.Recognition.Dtos;
using Microsoft.EntityFrameworkCore;

namespace LupiraMtgApi.Recognition.Application.Steps;

/// <summary>
/// Loads CardPrinting + set name for the top-N ranked candidates and produces the
/// <see cref="CardCandidateResponse"/> list returned to the client. Skips candidates
/// whose printing isn't in the DB (defensive against the printing being GC'd between
/// pHash index build and scan time). Preserves index alignment between
/// <see cref="ScanContext.Ranked"/> and <see cref="ScanContext.HydratedRows"/> so
/// ConfidenceStep can read its score state from the same row that's at position 0
/// of the response.
/// </summary>
public sealed class HydrateStep : IScanStep
{
    private readonly LupiraMtgDbContext _db;
    private readonly CardPrintingMapper _mapper;

    public HydrateStep(LupiraMtgDbContext db, CardPrintingMapper mapper)
    {
        _db = db;
        _mapper = mapper;
    }

    public string Name => "hydrate";

    public async Task<ScanContext> ExecuteAsync(ScanContext ctx, CancellationToken ct)
    {
        using var span = ScanTelemetry.Source.StartActivity("hydrate");

        if (ctx.TopRanked.Count == 0)
        {
            span?.SetTag("hydrate.count", 0);
            return ctx;
        }

        var topIds = ctx.TopRanked.Select(r => r.PrintingId).ToList();
        var printings = await _db.CardPrintings
            .AsNoTracking()
            .Where(p => topIds.Contains(p.Id))
            .ToListAsync(ct);
        var printingsById = printings.ToDictionary(p => p.Id, StringComparer.Ordinal);

        var setCodes = printings.Select(p => p.SetCode).Distinct().ToList();
        var setNames = await _db.Sets
            .AsNoTracking()
            .Where(s => setCodes.Contains(s.Code))
            .ToDictionaryAsync(s => s.Code, s => s.Name, ct);

        var ranked = new List<CardCandidateResponse>(ctx.TopRanked.Count);
        var hydratedRows = new List<RankedCandidate>(ctx.TopRanked.Count);

        foreach (var row in ctx.TopRanked)
        {
            if (!printingsById.TryGetValue(row.PrintingId, out var printing))
            {
                continue;
            }

            var setName = setNames.GetValueOrDefault(printing.SetCode, printing.SetCode);
            var printingResponse = await _mapper.MapAsync(printing, setName, ct);

            ranked.Add(new CardCandidateResponse
            {
                Printing = printingResponse,
                CombinedScore = row.FinalScore,
                OcrAggregateScore = row.ZoneScores?.AggregateScore ?? 0.0,
                NameScore = row.ZoneScores?.NameScore ?? 0.0,
                TypeLineScore = row.ZoneScores?.TypeLineScore ?? 0.0,
                RulesTextScore = row.ZoneScores?.RulesTextScore ?? 0.0,
                PowerToughnessScore = row.ZoneScores?.PowerToughnessScore ?? 0.0,
                BottomMetadataScore = row.ZoneScores?.BottomMetadataScore ?? 0.0,
                HammingScore = row.HammingScore,
                SetTypeWeight = row.SetTypeWeight,
                HammingDistance = row.HammingDistance,
                MatchedByPHash = row.HammingDistance.HasValue,
                MatchedByName = (row.ZoneScores?.NameScore ?? 0.0) > 0,
            });
            hydratedRows.Add(row);
        }

        span?.SetTag("hydrate.count", ranked.Count);
        return ctx with { Ranked = ranked, HydratedRows = hydratedRows };
    }
}
