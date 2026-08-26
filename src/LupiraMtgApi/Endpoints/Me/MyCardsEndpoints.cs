using LupiraMtgApi.Handlers;

namespace LupiraMtgApi.Endpoints.Me;

public static class MyCardsEndpoints
{
    public static IEndpointConventionBuilder MapMyCards(this IEndpointRouteBuilder app) =>
        app.MapGet("/me/cards", (
                HttpContext ctx,
                int? take,
                int? skip,
                MyCardsHandler h,
                CancellationToken ct) =>
            h.ListAsync(ctx, take, skip, ct))
            .WithTags("Me")
            .WithSummary("List every card the caller owns across all collections.")
            .WithDescription(
                """
                Sorted by card name (ascending). `take` 1–200 (default 50). `skip` for paging.
                Response includes `total` so the client can render "X of Y" headers.
                """)
            .Produces<CardInstanceListResponse>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status401Unauthorized);
}
