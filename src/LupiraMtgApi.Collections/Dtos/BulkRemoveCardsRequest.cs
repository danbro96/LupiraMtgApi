namespace LupiraMtgApi.Collections.Dtos;

public sealed class BulkRemoveCardsRequest
{
    public required IReadOnlyList<Guid> InstanceIds { get; set; }
}
