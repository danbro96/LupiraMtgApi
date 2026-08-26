namespace LupiraMtgApi.Collections.Dtos;

public sealed class SelectionResponse
{
    public required Guid Id { get; set; }

    public required IReadOnlyList<SelectionEntryDto> Cards { get; set; }

    public required DateTimeOffset CreatedAt { get; set; }

    public required DateTimeOffset ExpiresAt { get; set; }
}
