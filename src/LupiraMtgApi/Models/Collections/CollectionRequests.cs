namespace LupiraMtgApi.Models.Collections;

public sealed class CreateCollectionRequest
{
    public required string Name { get; set; }
}

public sealed class RenameCollectionRequest
{
    public required string Name { get; set; }
}

public sealed class AddCardToCollectionRequest
{
    public required string PrintingId { get; set; }

    public bool IsFoil { get; set; }

    public string Language { get; set; } = "en";

    public string Condition { get; set; } = "NM";
}

public sealed class MoveCardRequest
{
    public required Guid ToCollectionId { get; set; }
}

public sealed class CardListResponse
{
    public required IReadOnlyList<CardInstanceResponse> Cards { get; set; }
}
