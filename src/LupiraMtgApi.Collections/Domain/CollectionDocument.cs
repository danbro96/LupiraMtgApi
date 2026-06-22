namespace LupiraMtgApi.Collections.Domain;

public sealed class CollectionDocument
{
    public Guid Id { get; set; }

    public required string OwnerId { get; set; }

    public required string Name { get; set; }

    public bool IsRemoved { get; set; }

    public List<CardInstance> Cards { get; set; } = new();

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }
}

public sealed class CardInstance
{
    public required Guid InstanceId { get; set; }

    public required string PrintingId { get; set; }

    public required bool IsFoil { get; set; }

    public required string Language { get; set; }

    public required string Condition { get; set; }

    public DateTimeOffset AcquiredAt { get; set; }
}
