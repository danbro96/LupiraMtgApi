namespace LupiraMtgApi.Models.Cards;

public sealed class CardSearchResponse
{
    public required IReadOnlyList<CardPrintingResponse> Results { get; set; }

    public required int Total { get; set; }
}
