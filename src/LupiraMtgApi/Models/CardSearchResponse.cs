namespace LupiraMtgApi.Models;

public sealed class CardSearchResponse
{
    public required IReadOnlyList<CardPrintingResponse> Results { get; set; }

    public required int Total { get; set; }
}
