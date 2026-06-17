namespace LupiraMtgApi.Catalog.Domain;

public sealed class SetTypeWeight
{
    public required string SetType { get; set; }

    public required double Weight { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }
}
