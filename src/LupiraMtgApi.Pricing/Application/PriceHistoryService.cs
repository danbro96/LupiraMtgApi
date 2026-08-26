using LupiraMtgApi.Pricing.Data;
using LupiraMtgApi.Pricing.Dtos;
using Microsoft.EntityFrameworkCore;

namespace LupiraMtgApi.Pricing.Application;

/// <summary>
/// Reads the store-on-change price history for one printing, clamped to the retention window.
/// Returns null when the printing has no recorded points so the host can map it to 404.
/// </summary>
public sealed class PriceHistoryService
{
    private readonly PricingDbContext _db;
    private readonly PricingOptions _options;

    public PriceHistoryService(PricingDbContext db, IOptions<PricingOptions> options)
    {
        _db = db;
        _options = options.Value;
    }

    public async Task<CardPriceHistoryResponse?> GetAsync(
        string printingId,
        DateOnly? from,
        DateOnly? to,
        CancellationToken ct)
    {
        var floor = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-_options.HistoryRetentionDays));
        var effectiveFrom = from is { } f && f > floor ? f : floor;

        var query = _db.CardPricePoints
            .AsNoTracking()
            .Where(p => p.PrintingId == printingId && p.ObservedOn >= effectiveFrom);

        if (to is { } t)
        {
            query = query.Where(p => p.ObservedOn <= t);
        }

        var points = await query
            .OrderBy(p => p.ObservedOn)
            .Select(p => new CardPricePointDto
            {
                ObservedOn = p.ObservedOn,
                Eur = p.Eur,
                EurFoil = p.EurFoil,
            })
            .ToListAsync(ct);

        if (points.Count == 0)
        {
            return null;
        }

        return new CardPriceHistoryResponse
        {
            PrintingId = printingId,
            Points = points,
        };
    }
}
