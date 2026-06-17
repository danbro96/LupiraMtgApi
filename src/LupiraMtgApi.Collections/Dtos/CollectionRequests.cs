namespace LupiraMtgApi.Collections.Dtos;

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

public sealed class CardInstanceListResponse
{
    public required IReadOnlyList<CardInstanceResponse> Cards { get; set; }

    public required int Total { get; set; }
}

public sealed class BulkAddCardItem
{
    public required string PrintingId { get; set; }

    public bool IsFoil { get; set; }

    public string Language { get; set; } = "en";

    public string Condition { get; set; } = "NM";

    // Add this many identical instances. Default 1; clamped to [1, 50] per item.
    public int? Count { get; set; }
}

public sealed class BulkAddCardsRequest
{
    public required IReadOnlyList<BulkAddCardItem> Items { get; set; }
}

public sealed class BulkAddCardsResponse
{
    public required IReadOnlyList<CardInstanceResponse> Added { get; set; }
}

public sealed class BulkRemoveCardsRequest
{
    public required IReadOnlyList<Guid> InstanceIds { get; set; }
}

public sealed class BulkRemoveCardsResponse
{
    public required int RemovedCount { get; set; }

    // InstanceIds in the request that did not exist in the source collection (no-op for those).
    public required int MissingCount { get; set; }
}

public sealed class BulkMoveCardsRequest
{
    public required IReadOnlyList<Guid> InstanceIds { get; set; }

    public required Guid ToCollectionId { get; set; }
}

public sealed class BulkMoveCardsResponse
{
    public required IReadOnlyList<CardInstanceResponse> Moved { get; set; }

    public required int MissingCount { get; set; }
}
