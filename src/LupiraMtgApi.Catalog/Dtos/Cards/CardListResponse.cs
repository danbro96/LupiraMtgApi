namespace LupiraMtgApi.Catalog.Dtos.Cards;

public sealed class CardListResponse
{
    public required IReadOnlyList<CardDto> Results { get; set; }

    public required int Total { get; set; }
}
