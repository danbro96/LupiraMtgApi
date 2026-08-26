namespace LupiraMtgApi.Catalog.Dtos.Admin;

public sealed class SetTypeWeightListResponse
{
    public required IReadOnlyList<SetTypeWeightDto> Weights { get; set; }
}
