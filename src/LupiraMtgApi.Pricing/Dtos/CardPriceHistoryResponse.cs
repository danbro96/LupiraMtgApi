namespace LupiraMtgApi.Pricing.Dtos;

public sealed class CardPriceHistoryResponse
{
    public required string PrintingId { get; set; }

    public required IReadOnlyList<CardPricePointResponse> Points { get; set; }
}
