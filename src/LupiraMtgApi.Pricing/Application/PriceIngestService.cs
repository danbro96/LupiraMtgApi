using LupiraMtgApi.Pricing.Data;
using LupiraMtgApi.Pricing.Domain;
using Microsoft.EntityFrameworkCore;

namespace LupiraMtgApi.Pricing.Application;

/// <summary>One printing's EUR prices as observed by an ingest run.</summary>
public sealed class PriceObservation
{
    public required string PrintingId { get; set; }

    public decimal? Eur { get; set; }

    public decimal? EurFoil { get; set; }
}

/// <summary>
/// Writes a batch of price observations: upserts <c>card_prices_latest</c> and appends a
/// <c>card_price_points</c> row only when the value changed since the prior latest (store-on-change).
/// O(1) queries per batch — two batched reads, no per-row I/O. Source-agnostic: the caller supplies
/// the provenance per batch via <paramref name="source"/> (the host Scryfall sync today; an
/// MTGJSON/Cardmarket feed could call the same method later).
/// </summary>
public sealed class PriceIngestService
{
    private readonly PricingDbContext _db;

    public PriceIngestService(PricingDbContext db)
    {
        _db = db;
    }

    public async Task IngestBatchAsync(
        IReadOnlyList<PriceObservation> batch,
        DateOnly observedOn,
        string source,
        CancellationToken ct)
    {
        if (batch.Count == 0)
        {
            return;
        }

        // Last observation wins per printing (the sync may surface the same id more than once).
        var deduped = batch
            .GroupBy(o => o.PrintingId)
            .ToDictionary(g => g.Key, g => g.Last());
        var ids = deduped.Keys.ToList();

        var latestById = await _db.CardPricesLatest
            .Where(p => ids.Contains(p.PrintingId))
            .ToDictionaryAsync(p => p.PrintingId, ct);

        var todaysPointsById = await _db.CardPricePoints
            .Where(p => ids.Contains(p.PrintingId) && p.ObservedOn == observedOn)
            .ToDictionaryAsync(p => p.PrintingId, ct);

        var now = DateTimeOffset.UtcNow;

        foreach (var (id, obs) in deduped)
        {
            var hasValue = obs.Eur is not null || obs.EurFoil is not null;
            latestById.TryGetValue(id, out var latest);

            // Nothing to record and nothing to update — skip rather than write a null row.
            if (!hasValue && latest is null)
            {
                continue;
            }

            var changed = latest is null
                ? hasValue
                : latest.Eur != obs.Eur || latest.EurFoil != obs.EurFoil;

            if (latest is null)
            {
                _db.CardPricesLatest.Add(new CardPriceLatest
                {
                    PrintingId = id,
                    Eur = obs.Eur,
                    EurFoil = obs.EurFoil,
                    UpdatedAt = now,
                });
            }
            else
            {
                latest.Eur = obs.Eur;
                latest.EurFoil = obs.EurFoil;
                latest.UpdatedAt = now;
            }

            if (!changed)
            {
                continue;
            }

            if (todaysPointsById.TryGetValue(id, out var point))
            {
                point.Eur = obs.Eur;
                point.EurFoil = obs.EurFoil;
                point.Source = source;
            }
            else
            {
                _db.CardPricePoints.Add(new CardPricePoint
                {
                    PrintingId = id,
                    ObservedOn = observedOn,
                    Eur = obs.Eur,
                    EurFoil = obs.EurFoil,
                    Source = source,
                });
            }
        }

        await _db.SaveChangesAsync(ct);
    }
}
