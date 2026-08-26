namespace LupiraMtgApi.Collections.Dtos;

public sealed class BulkMoveCardsResponse
{
    public required IReadOnlyList<CardInstanceDto> Moved { get; set; }

    public required int MissingCount { get; set; }
}
