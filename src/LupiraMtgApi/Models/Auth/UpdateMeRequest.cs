namespace LupiraMtgApi.Models.Auth;

public sealed class UpdateMeRequest
{
    // Null clears the current name; empty/whitespace also clears. Non-empty trims and stores.
    public string? DisplayName { get; set; }
}
