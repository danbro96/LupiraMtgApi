namespace LupiraMtgApi.Collections.Dtos;

public sealed class CommitSelectionResponse
{
    public required Guid CollectionId { get; set; }

    public required string CollectionName { get; set; }

    public required int AddedCount { get; set; }

    public required int RemainingCount { get; set; }
}
