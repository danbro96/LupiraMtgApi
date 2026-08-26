using LupiraMtgApi.Dtos.Auth;
using LupiraMtgApi.Handlers;

namespace LupiraMtgApi.Endpoints.Me;

public static class WhoAmIEndpoints
{
    public static IEndpointConventionBuilder MapWhoAmI(this IEndpointRouteBuilder app) =>
        app.MapGet("/me", (HttpContext ctx, MeHandler handler) => handler.WhoAmI(ctx))
            .WithTags("Me")
            .WithSummary("Return the caller's identity.")
            .WithDescription(
                """
                Projects the validated Authentik access token's claims: `subject` (the caller's
                email — the identity that owns collections/selections/scans), `displayName` (the
                `name` claim), and `isAdmin` (derived from the `groups` claim). Useful as a token
                sanity-check from the mobile client on cold start.
                """)
            .Produces<WhoAmIResponse>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status401Unauthorized)
            .WithName("WhoAmI");
}
