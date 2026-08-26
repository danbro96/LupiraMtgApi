namespace LupiraMtgApi.Catalog.Dtos.Sets;

public sealed class SetListResponse
{
    public required IReadOnlyList<SetDto> Results { get; set; }

    public required int Total { get; set; }
}
