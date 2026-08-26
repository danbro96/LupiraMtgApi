namespace LupiraMtgApi.Collections.Dtos;

public sealed class CollectionDto
{
    public required Guid Id { get; set; }

    public required string Name { get; set; }

    public required int CardCount { get; set; }

    public required DateTimeOffset CreatedAt { get; set; }

    public required DateTimeOffset UpdatedAt { get; set; }
}
