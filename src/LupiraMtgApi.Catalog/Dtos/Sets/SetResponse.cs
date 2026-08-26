namespace LupiraMtgApi.Catalog.Dtos.Sets;

public sealed class SetResponse
{
    public required string Code { get; set; }

    public required string Name { get; set; }

    public required string SetType { get; set; }

    public required DateOnly? ReleasedAt { get; set; }

    public required int CardCount { get; set; }

    public required string? IconUrl { get; set; }
}
