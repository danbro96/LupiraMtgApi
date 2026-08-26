namespace LupiraMtgApi.Catalog.Dtos.Admin;

public sealed class SetTypeWeightDto
{
    public required string SetType { get; set; }

    public required double Weight { get; set; }

    public required DateTimeOffset UpdatedAt { get; set; }
}
