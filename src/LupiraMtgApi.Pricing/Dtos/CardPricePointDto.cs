namespace LupiraMtgApi.Pricing.Dtos;

public sealed class CardPricePointDto
{
    public required DateOnly ObservedOn { get; set; }

    public required decimal? Eur { get; set; }

    public required decimal? EurFoil { get; set; }
}
