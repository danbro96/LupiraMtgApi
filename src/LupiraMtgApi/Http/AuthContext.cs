using System.Security.Claims;

namespace LupiraMtgApi.Http;

internal static class AuthContext
{
    // The OIDC subject (email, per the Authentik provider's subject mode) — the identity every
    // owned Marten document is keyed by.
    public static bool TryGetOwnerId(this HttpContext context, out string ownerId)
    {
        ownerId = context.User.FindFirstValue("sub") ?? string.Empty;
        return ownerId.Length > 0;
    }
}
