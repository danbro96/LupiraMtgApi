namespace LupiraMtgApi.Models;

public sealed class WhoAmIResponse
{
    public required Guid Sub { get; set; }

    public string? DisplayName { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset LastSeenAt { get; set; }
}
