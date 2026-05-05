namespace LupiraMtgApi.Models.Auth;

public sealed class WhoAmIResponse
{
    public required Guid Id { get; set; }

    public string? DisplayName { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset LastSeenAt { get; set; }
}
