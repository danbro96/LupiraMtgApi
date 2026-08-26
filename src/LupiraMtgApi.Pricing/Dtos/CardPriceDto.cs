namespace LupiraMtgApi.Pricing.Dtos;

/// <summary>Latest EUR price for a printing. Null fields = no price recorded for that finish.</summary>
public sealed class CardPriceDto
{
    public required decimal? Eur { get; set; }

    public required decimal? EurFoil { get; set; }

    public required DateTimeOffset? UpdatedAt { get; set; }
}
