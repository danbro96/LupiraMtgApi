namespace LupiraMtgApi.Pricing.Domain;

/// <summary>
/// Append-only daily price history point. Written store-on-change: a row exists for a
/// <c>(PrintingId, ObservedOn)</c> only when that day's value differed from the prior latest, so the
/// table stays sparse instead of one row per printing per day.
/// </summary>
public sealed class CardPricePoint
{
    public required string PrintingId { get; set; }

    public DateOnly ObservedOn { get; set; }

    public decimal? Eur { get; set; }

    public decimal? EurFoil { get; set; }

    public required string Source { get; set; }
}
