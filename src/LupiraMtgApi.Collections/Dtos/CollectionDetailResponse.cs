namespace LupiraMtgApi.Collections.Dtos;

public sealed class CollectionDetailResponse
{
    public required Guid Id { get; set; }

    public required string Name { get; set; }

    public required IReadOnlyList<CardInstanceResponse> Cards { get; set; }

    public required DateTimeOffset CreatedAt { get; set; }

    public required DateTimeOffset UpdatedAt { get; set; }
}
