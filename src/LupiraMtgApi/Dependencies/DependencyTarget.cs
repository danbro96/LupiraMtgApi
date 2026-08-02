using LupiraMtgApi.Catalog.Infrastructure.Storage;
using LupiraMtgApi.Recognition.Infrastructure.Ocr;

namespace LupiraMtgApi.Dependencies;

/// <summary>One outward edge: an optional <c>X-API-Key</c> is the only auth this repo's downstreams use.</summary>
public sealed class DependencyTarget
{
    public required string Name { get; set; }
    public required string BaseUrl { get; set; }
    public required string ProbePath { get; set; }
    public string? ApiKey { get; set; }
}

/// <summary>Roster derived from the same options the real clients bind — edges cannot drift.
/// MinIO is probed on its anonymous health endpoint (S3 auth needs signed requests), so that edge
/// covers reachability only.</summary>
public static class DependencyTargets
{
    public static IReadOnlyList<DependencyTarget> From(FlorenceOcrOptions florence, MinioImageStoreOptions minio) =>
    [
        new DependencyTarget
        {
            Name = "florence-api",
            BaseUrl = florence.Url,
            ProbePath = "options",
            ApiKey = string.IsNullOrWhiteSpace(florence.ApiKey) ? null : florence.ApiKey,
        },
        new DependencyTarget
        {
            Name = "scryfall",
            BaseUrl = "https://api.scryfall.com/",
            ProbePath = "bulk-data",
        },
        new DependencyTarget
        {
            Name = "minio",
            BaseUrl = string.IsNullOrWhiteSpace(minio.Endpoint) ? "" : $"{(minio.UseSsl ? "https" : "http")}://{minio.Endpoint}/",
            ProbePath = "minio/health/live",
        },
    ];
}
