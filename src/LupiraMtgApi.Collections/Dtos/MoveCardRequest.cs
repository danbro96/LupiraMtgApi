namespace LupiraMtgApi.Collections.Dtos;

public sealed class MoveCardRequest
{
    public required Guid ToCollectionId { get; set; }
}
