namespace LupiraMtgApi.Models.Auth;

public sealed class RegisterDeviceResponse
{
    public required Guid Id { get; set; }

    public required string Token { get; set; }

    public string? DisplayName { get; set; }
}
