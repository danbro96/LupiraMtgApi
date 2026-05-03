using Microsoft.AspNetCore.Authentication;

namespace LupiraMtgApi.Auth;

public sealed class DeviceTokenAuthOptions : AuthenticationSchemeOptions
{
    public const string SchemeName = "DeviceToken";

    public const string HeaderName = "Authorization";

    /// <summary>
    /// How often to bump <c>DeviceUser.LastSeenAt</c> per request. Set higher to reduce
    /// write traffic; set to <c>TimeSpan.Zero</c> to bump on every request.
    /// </summary>
    public TimeSpan LastSeenWriteInterval { get; set; } = TimeSpan.FromMinutes(15);
}
