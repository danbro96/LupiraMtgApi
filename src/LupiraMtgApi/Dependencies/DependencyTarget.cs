namespace LupiraMtgApi.Dependencies;

/// <summary>One outward edge: an optional <c>X-API-Key</c> is the only auth this repo's downstreams use.</summary>
public sealed class DependencyTarget
{
    public required string Name { get; set; }
    public required string BaseUrl { get; set; }
    public required string ProbePath { get; set; }
    public string? ApiKey { get; set; }
}
