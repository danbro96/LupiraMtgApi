namespace LupiraMtgApi.Domain.Collection;

public sealed class CollectionDocument
{
    public Guid Id { get; set; }

    public required string OwnerSub { get; set; }

    public required string Name { get; set; }

    public bool Removed { get; set; }

    public List<CardInstance> Cards { get; set; } = new();

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }
}

public sealed class CardInstance
{
    public required Guid InstanceId { get; set; }

    public required string PrintingId { get; set; }

    public required bool Foil { get; set; }

    public required string Language { get; set; }

    public required string Condition { get; set; }

    public DateTimeOffset AcquiredAt { get; set; }
}
