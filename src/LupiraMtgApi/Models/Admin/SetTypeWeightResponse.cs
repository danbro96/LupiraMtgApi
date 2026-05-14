namespace LupiraMtgApi.Models.Admin;

public sealed class SetTypeWeightResponse
{
    public required string SetType { get; set; }

    public required double Weight { get; set; }

    public required DateTimeOffset UpdatedAt { get; set; }
}

public sealed class SetTypeWeightListResponse
{
    public required IReadOnlyList<SetTypeWeightResponse> Weights { get; set; }
}

public sealed class UpdateSetTypeWeightRequest
{
    public required double Weight { get; set; }
}
