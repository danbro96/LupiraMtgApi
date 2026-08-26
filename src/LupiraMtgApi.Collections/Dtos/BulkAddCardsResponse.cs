namespace LupiraMtgApi.Collections.Dtos;

public sealed class BulkAddCardsResponse
{
    public required IReadOnlyList<CardInstanceResponse> Added { get; set; }
}
