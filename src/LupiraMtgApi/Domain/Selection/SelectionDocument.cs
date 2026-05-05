namespace LupiraMtgApi.Domain.Selection;

public sealed class SelectionDocument
{
    public required Guid Id { get; set; }

    public required Guid OwnerId { get; set; }

    public List<SelectionEntry> Cards { get; set; } = new();

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset ExpiresAt { get; set; }
}

public sealed class SelectionEntry
{
    public required Guid InstanceId { get; set; }

    public required string PrintingId { get; set; }

    public required bool IsFoil { get; set; }

    public required string Language { get; set; }

    public required string Condition { get; set; }

    public required double Confidence { get; set; }
}
