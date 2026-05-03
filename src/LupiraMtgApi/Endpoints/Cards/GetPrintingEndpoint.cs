using LupiraMtgApi.Handlers;

namespace LupiraMtgApi.Endpoints.Cards;

public static class GetPrintingEndpoint
{
    public static IEndpointConventionBuilder MapGetPrinting(this IEndpointRouteBuilder app) =>
        app.MapGet("/cards/{printingId}", (
                string printingId,
                CardSearchHandler handler,
                CancellationToken ct) =>
            handler.GetByIdAsync(printingId, ct))
            .WithTags("Cards")
            .WithSummary("Get a single card printing by Scryfall ID.")
            .WithDescription(
                """
                Returns a printing's metadata along with presigned URLs for the normal-size image
                and the art crop (if present in the local image store). Returns 404 if the printing
                is unknown to the local catalog (re-run sync if it should exist).
                """);
}
