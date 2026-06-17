namespace LupiraMtgApi.Catalog.Dtos.Cards;

public sealed class CardPrintingListResponse
{
    public required IReadOnlyList<CardPrintingResponse> Results { get; set; }
}
