using LupiraMtgApi.Catalog.Dtos.Cards;

namespace LupiraMtgApi.Collections.Dtos;

public sealed class SelectionResponse
{
    public required Guid Id { get; set; }

    public required IReadOnlyList<SelectionEntryResponse> Cards { get; set; }

    public required DateTimeOffset CreatedAt { get; set; }

    public required DateTimeOffset ExpiresAt { get; set; }
}

public sealed class SelectionEntryResponse
{
    public required Guid InstanceId { get; set; }

    public required CardPrintingResponse Printing { get; set; }

    public required bool IsFoil { get; set; }

    public required string Language { get; set; }

    public required string Condition { get; set; }

    public required double Confidence { get; set; }
}

public sealed class AddSelectionEntryRequest
{
    public required string PrintingId { get; set; }

    public bool IsFoil { get; set; }

    public string Language { get; set; } = "en";

    public string Condition { get; set; } = "NM";

    public double Confidence { get; set; } = 1.0;

    public bool AllowDuplicate { get; set; }
}

public sealed class CommitSelectionRequest
{
    public required Guid CollectionId { get; set; }

    /// <summary>
    /// Optional subset of <see cref="SelectionEntryResponse.InstanceId"/> values to commit.
    /// When null or empty, commits every card in the selection.
    /// </summary>
    public IReadOnlyList<Guid>? InstanceIds { get; set; }
}

public sealed class CommitSelectionResponse
{
    public required Guid CollectionId { get; set; }

    public required string CollectionName { get; set; }

    public required int AddedCount { get; set; }

    public required int RemainingCount { get; set; }
}
