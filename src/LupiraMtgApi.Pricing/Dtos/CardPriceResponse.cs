namespace LupiraMtgApi.Pricing.Dtos;

/// <summary>Latest EUR price for a printing. Null fields = no price recorded for that finish.</summary>
public sealed class CardPriceResponse
{
    public decimal? Eur { get; set; }

    public decimal? EurFoil { get; set; }

    public DateTimeOffset? UpdatedAt { get; set; }
}
