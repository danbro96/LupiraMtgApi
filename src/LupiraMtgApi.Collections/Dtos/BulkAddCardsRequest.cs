namespace LupiraMtgApi.Collections.Dtos;

public sealed class BulkAddCardsRequest
{
    public required IReadOnlyList<BulkAddCardItem> Items { get; set; }
}
