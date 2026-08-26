namespace LupiraMtgApi.Recognition.Infrastructure.Imaging;

public readonly record struct PHashResult(long? Hash, int LatencyMs, IReadOnlyList<PHashIndex.PHashHit> Hits)
{
    public static PHashResult Empty => new(null, 0, Array.Empty<PHashIndex.PHashHit>());
}
