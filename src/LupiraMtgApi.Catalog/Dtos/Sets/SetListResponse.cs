namespace LupiraMtgApi.Catalog.Dtos.Sets;

public sealed class SetListResponse
{
    public required IReadOnlyList<SetResponse> Results { get; set; }

    public required int Total { get; set; }
}
