namespace LupiraMtgApi.Collections.Dtos;

public sealed class BulkRemoveCardsResponse
{
    public required int RemovedCount { get; set; }

    // InstanceIds in the request that did not exist in the source collection (no-op for those).
    public required int MissingCount { get; set; }
}
