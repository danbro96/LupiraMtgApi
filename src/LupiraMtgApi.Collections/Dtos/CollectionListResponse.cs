namespace LupiraMtgApi.Collections.Dtos;

public sealed class CollectionListResponse
{
    public required IReadOnlyList<CollectionDto> Collections { get; set; }
}
