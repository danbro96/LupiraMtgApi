namespace LupiraMtgApi.Pricing.Application;

/// <summary>One printing's EUR prices as observed by an ingest run.</summary>
public sealed class PriceObservation
{
    public required string PrintingId { get; set; }

    public decimal? Eur { get; set; }

    public decimal? EurFoil { get; set; }
}
