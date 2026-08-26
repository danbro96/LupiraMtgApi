namespace LupiraMtgApi.Models.Auth;

public sealed class WhoAmIResponse
{
    /// <summary>The OIDC subject — the caller's email, which every owned document is keyed by.</summary>
    public required string Subject { get; set; }

    public required string? DisplayName { get; set; }

    public required bool IsAdmin { get; set; }
}
