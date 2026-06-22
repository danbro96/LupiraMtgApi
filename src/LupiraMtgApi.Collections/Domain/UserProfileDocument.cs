namespace LupiraMtgApi.Collections.Domain;

public sealed class UserProfileDocument
{
    // The OIDC subject (email) — same identity the owned documents are keyed by.
    public required string Id { get; set; }

    public string? DisplayName { get; set; }

    public Guid? DefaultCollectionId { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset? LastSeenAt { get; set; }
}
