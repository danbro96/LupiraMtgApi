namespace LupiraMtgApi.Collections.Dtos;

public sealed class CardInstanceListResponse
{
    public required IReadOnlyList<CardInstanceResponse> Cards { get; set; }

    public required int Total { get; set; }
}
