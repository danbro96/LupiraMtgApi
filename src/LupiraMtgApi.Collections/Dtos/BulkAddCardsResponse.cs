namespace LupiraMtgApi.Collections.Dtos;

public sealed class BulkAddCardsResponse
{
    public required IReadOnlyList<CardInstanceDto> Added { get; set; }
}
