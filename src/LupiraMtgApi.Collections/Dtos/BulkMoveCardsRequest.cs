namespace LupiraMtgApi.Collections.Dtos;

public sealed class BulkMoveCardsRequest
{
    public required IReadOnlyList<Guid> InstanceIds { get; set; }

    public required Guid ToCollectionId { get; set; }
}
