namespace LupiraMtgApi.Models.Cards;

public sealed class CardListResponse
{
    public required IReadOnlyList<CardResponse> Results { get; set; }

    public required int Total { get; set; }
}
