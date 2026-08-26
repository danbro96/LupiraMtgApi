namespace LupiraMtgApi.Collections.Dtos;

public sealed class CardInstanceListResponse
{
    public required IReadOnlyList<CardInstanceDto> Cards { get; set; }

    public required int Total { get; set; }
}
