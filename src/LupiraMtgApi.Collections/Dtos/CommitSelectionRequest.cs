namespace LupiraMtgApi.Collections.Dtos;

public sealed class CommitSelectionRequest
{
    public required Guid CollectionId { get; set; }

    /// <summary>
    /// Optional subset of <see cref="SelectionEntryDto.InstanceId"/> values to commit.
    /// When null or empty, commits every card in the selection.
    /// </summary>
    public IReadOnlyList<Guid>? InstanceIds { get; set; }
}
