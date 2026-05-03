namespace LupiraMtgApi.Models.Collections;

public sealed class CollectionListResponse
{
    public required IReadOnlyList<CollectionResponse> Collections { get; set; }
}
