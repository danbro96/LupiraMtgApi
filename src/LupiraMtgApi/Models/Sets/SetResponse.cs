namespace LupiraMtgApi.Models.Sets;

public sealed class SetResponse
{
    public required string Code { get; set; }

    public required string Name { get; set; }

    public required string SetType { get; set; }

    public DateOnly? ReleasedAt { get; set; }

    public int CardCount { get; set; }

    public string? IconUrl { get; set; }
}
