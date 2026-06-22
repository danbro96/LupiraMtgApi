namespace LupiraMtgApi.Models.Auth;

public sealed class WhoAmIResponse
{
    /// <summary>The OIDC subject — the caller's email, which every owned document is keyed by.</summary>
    public required string Subject { get; set; }

    public string? DisplayName { get; set; }

    public bool IsAdmin { get; set; }
}
