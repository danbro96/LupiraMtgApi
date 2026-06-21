namespace LupiraMtgApi.Pricing.Dtos;

public sealed class CardPricePointResponse
{
    public required DateOnly ObservedOn { get; set; }

    public decimal? Eur { get; set; }

    public decimal? EurFoil { get; set; }
}
