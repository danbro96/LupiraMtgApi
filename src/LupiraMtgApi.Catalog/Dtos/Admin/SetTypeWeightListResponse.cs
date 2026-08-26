namespace LupiraMtgApi.Catalog.Dtos.Admin;

public sealed class SetTypeWeightListResponse
{
    public required IReadOnlyList<SetTypeWeightResponse> Weights { get; set; }
}
