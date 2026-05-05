using System.Security.Claims;

namespace LupiraMtgApi.Handlers;

internal static class AuthContext
{
    public static bool TryGetOwnerId(this HttpContext context, out Guid ownerId)
    {
        var raw = context.User.FindFirstValue("sub");
        return Guid.TryParse(raw, out ownerId);
    }
}
