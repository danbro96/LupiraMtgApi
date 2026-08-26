namespace LupiraMtgApi.Catalog.Dtos.Cards;

public sealed class CardPrintingListResponse
{
    public required IReadOnlyList<CardPrintingDto> Results { get; set; }
}
