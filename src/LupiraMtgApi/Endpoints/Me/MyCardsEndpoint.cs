using LupiraMtgApi.Handlers;

namespace LupiraMtgApi.Endpoints.Me;

public static class MyCardsEndpoint
{
    public static IEndpointConventionBuilder MapMyCards(this IEndpointRouteBuilder app) =>
        app.MapGet("/me/cards", (HttpContext ctx, MyCardsHandler h, CancellationToken ct) => h.ListAsync(ctx, ct))
            .WithTags("Me")
            .WithSummary("List every card the caller owns across all collections.");
}
