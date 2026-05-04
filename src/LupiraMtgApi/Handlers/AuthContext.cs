using System.Security.Claims;

namespace LupiraMtgApi.Handlers;

internal static class AuthContext
{
    public static bool TryGetOwnerSub(this HttpContext context, out string ownerSub)
    {
        ownerSub = context.User.FindFirstValue("sub") ?? string.Empty;
        return !string.IsNullOrEmpty(ownerSub);
    }
}
