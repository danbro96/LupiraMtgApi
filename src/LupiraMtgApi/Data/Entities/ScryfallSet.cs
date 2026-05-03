namespace LupiraMtgApi.Data.Entities;

public sealed class ScryfallSet
{
    public required string Code { get; set; }

    public required string Name { get; set; }

    public required string SetType { get; set; }

    public DateOnly? ReleasedAt { get; set; }

    public int CardCount { get; set; }

    public string? IconSvgUri { get; set; }

    public DateTimeOffset SyncedAt { get; set; }
}
