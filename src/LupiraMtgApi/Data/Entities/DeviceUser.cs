namespace LupiraMtgApi.Data.Entities;

public sealed class DeviceUser
{
    public Guid Id { get; set; }

    public required string TokenHash { get; set; }

    public string? DisplayName { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset LastSeenAt { get; set; }
}
