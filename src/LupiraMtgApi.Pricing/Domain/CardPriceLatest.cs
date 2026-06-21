namespace LupiraMtgApi.Pricing.Domain;

/// <summary>
/// Current price snapshot for a printing — one row per Scryfall printing id. Upserted every sync.
/// The hot read path (<c>CardPriceLookup</c>) hits only this table.
/// </summary>
public sealed class CardPriceLatest
{
    public required string PrintingId { get; set; }

    public decimal? Eur { get; set; }

    public decimal? EurFoil { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }
}
