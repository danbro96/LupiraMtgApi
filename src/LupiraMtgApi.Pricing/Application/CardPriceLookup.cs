using LupiraMtgApi.Pricing.Data;
using LupiraMtgApi.Pricing.Dtos;
using Microsoft.EntityFrameworkCore;

namespace LupiraMtgApi.Pricing.Application;

/// <summary>
/// Batch reader for the latest price of a set of printings. Callers (catalog/collection hydration)
/// pre-fetch once per list so a page of cards costs a single query, never N+1.
/// </summary>
public sealed class CardPriceLookup
{
    private readonly PricingDbContext _db;

    public CardPriceLookup(PricingDbContext db)
    {
        _db = db;
    }

    public async Task<IReadOnlyDictionary<string, CardPriceResponse>> GetAsync(
        IEnumerable<string> printingIds,
        CancellationToken ct)
    {
        var ids = printingIds.Distinct().ToList();
        if (ids.Count == 0)
        {
            return new Dictionary<string, CardPriceResponse>();
        }

        return await _db.CardPricesLatest
            .AsNoTracking()
            .Where(p => ids.Contains(p.PrintingId))
            .ToDictionaryAsync(
                p => p.PrintingId,
                p => new CardPriceResponse
                {
                    Eur = p.Eur,
                    EurFoil = p.EurFoil,
                    UpdatedAt = p.UpdatedAt,
                },
                ct);
    }
}
