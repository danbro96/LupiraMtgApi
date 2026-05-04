namespace LupiraMtgApi.Models.Auth;

public sealed class RegisterDeviceResponse
{
    public required Guid Sub { get; set; }

    public required string Token { get; set; }

    public string? DisplayName { get; set; }
}
