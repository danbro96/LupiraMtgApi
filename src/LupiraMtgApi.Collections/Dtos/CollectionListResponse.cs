namespace LupiraMtgApi.Collections.Dtos;

public sealed class CollectionListResponse
{
    public required IReadOnlyList<CollectionResponse> Collections { get; set; }
}
