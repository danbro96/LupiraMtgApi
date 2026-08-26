using System.Security.Claims;
using LupiraMtgApi.Dtos.Auth;
using LupiraMtgApi.Http;
using Microsoft.AspNetCore.Http.HttpResults;

namespace LupiraMtgApi.Handlers;

public sealed class MeHandler
{
    // Group membership that grants admin in this app. Authentik emits group names in the `groups`
    // claim (via the default profile mapping); platform-admins are global, mtg-admins app-scoped.
    private static readonly string[] AdminGroups = ["mtg-admins", "platform-admins"];

    public Results<Ok<WhoAmIResponse>, UnauthorizedHttpResult> WhoAmI(HttpContext httpContext)
    {
        if (!httpContext.TryGetOwnerId(out var subject))
        {
            return TypedResults.Unauthorized();
        }

        var user = httpContext.User;
        var isAdmin = user.FindAll("groups").Any(c => AdminGroups.Contains(c.Value));

        return TypedResults.Ok(new WhoAmIResponse
        {
            Subject = subject,
            DisplayName = user.FindFirstValue("name"),
            IsAdmin = isAdmin,
        });
    }
}
