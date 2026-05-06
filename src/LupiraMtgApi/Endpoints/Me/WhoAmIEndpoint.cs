using LupiraMtgApi.Handlers;
using LupiraMtgApi.Models.Auth;

namespace LupiraMtgApi.Endpoints.Me;

public static class WhoAmIEndpoint
{
    public static IEndpointConventionBuilder MapWhoAmI(this IEndpointRouteBuilder app) =>
        app.MapGet("/me", (HttpContext ctx, MeHandler handler, CancellationToken ct) =>
                handler.WhoAmIAsync(ctx, ct))
            .WithTags("Me")
            .WithSummary("Return the caller's device identity.")
            .WithDescription(
                """
                Resolves the bearer token to its `sub` and returns the device's profile fields.
                Useful as a smoke test for token validity from the mobile client (sanity-check
                after registration or on app cold start).
                """)
            .Produces<WhoAmIResponse>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status401Unauthorized);
}
