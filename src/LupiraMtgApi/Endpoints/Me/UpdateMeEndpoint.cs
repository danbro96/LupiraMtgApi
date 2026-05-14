using LupiraMtgApi.Handlers;
using LupiraMtgApi.Models.Auth;

namespace LupiraMtgApi.Endpoints.Me;

public static class UpdateMeEndpoint
{
    public static IEndpointConventionBuilder MapUpdateMe(this IEndpointRouteBuilder app) =>
        app.MapPatch("/me", (
                HttpContext ctx,
                UpdateMeRequest? body,
                MeHandler handler,
                CancellationToken ct) =>
            handler.UpdateAsync(ctx, body, ct))
            .WithTags("Me")
            .WithSummary("Update the caller's device profile.")
            .WithDescription(
                """
                Currently only `displayName` is editable. Pass `null` or `""` to clear the
                current name. The token is unchanged — this is a profile edit, not a re-issuance.
                Returns the updated device profile in the same shape as `GET /me`.
                """)
            .Produces<WhoAmIResponse>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status401Unauthorized);
}
