using LupiraMtgApi.Catalog.Infrastructure.Storage;
using LupiraMtgApi.Recognition.Infrastructure.Ocr;

namespace LupiraMtgApi.Dependencies;

/// <summary>Roster derived from the same options the real clients bind — edges cannot drift.
/// Garage is probed on its anonymous admin health endpoint (S3 auth needs signed requests), so that
/// edge covers reachability only.</summary>
public static class DependencyTargets
{
    public static IReadOnlyList<DependencyTarget> From(FlorenceOcrOptions florence, S3ImageStoreOptions s3) =>
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
            Name = "garage",
            BaseUrl = string.IsNullOrWhiteSpace(s3.HealthUrl) ? "" : s3.HealthUrl.TrimEnd('/') + "/",
            ProbePath = "health",
        },
    ];
}
