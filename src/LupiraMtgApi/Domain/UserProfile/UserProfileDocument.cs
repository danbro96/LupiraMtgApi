namespace LupiraMtgApi.Domain.UserProfile;

public sealed class UserProfileDocument
{
    public required string Sub { get; set; }

    public string? DisplayName { get; set; }

    public Guid? DefaultCollectionId { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset? LastSeenAt { get; set; }
}
