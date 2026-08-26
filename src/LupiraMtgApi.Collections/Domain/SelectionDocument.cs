namespace LupiraMtgApi.Collections.Domain;

public sealed class SelectionDocument
{
    public required Guid Id { get; set; }

    public required string OwnerId { get; set; }

    public List<SelectionEntry> Cards { get; set; } = new();

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset ExpiresAt { get; set; }
}
