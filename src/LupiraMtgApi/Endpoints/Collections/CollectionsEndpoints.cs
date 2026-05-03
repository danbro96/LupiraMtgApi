using LupiraMtgApi.Handlers;
using LupiraMtgApi.Models.Collections;

namespace LupiraMtgApi.Endpoints.Collections;

public static class CollectionsEndpoints
{
    public static IEndpointRouteBuilder MapCollections(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/collections")
            .RequireAuthorization()
            .WithTags("Collections");

        group.MapGet("/", (HttpContext ctx, CollectionsHandler h, CancellationToken ct) => h.ListAsync(ctx, ct))
            .WithSummary("List the caller's collections.");

        group.MapPost("/", (HttpContext ctx, CreateCollectionRequest body, CollectionsHandler h, CancellationToken ct) =>
                h.CreateAsync(ctx, body, ct))
            .WithSummary("Create a new collection.");

        group.MapGet("/{collectionId:guid}", (HttpContext ctx, Guid collectionId, CollectionsHandler h, CancellationToken ct) =>
                h.GetAsync(ctx, collectionId, ct))
            .WithSummary("Get a collection with its cards.");

        group.MapPatch("/{collectionId:guid}", (HttpContext ctx, Guid collectionId, RenameCollectionRequest body, CollectionsHandler h, CancellationToken ct) =>
                h.RenameAsync(ctx, collectionId, body, ct))
            .WithSummary("Rename a collection.");

        group.MapDelete("/{collectionId:guid}", (HttpContext ctx, Guid collectionId, CollectionsHandler h, CancellationToken ct) =>
                h.DeleteAsync(ctx, collectionId, ct))
            .WithSummary("Soft-delete a collection.");

        group.MapGet("/{collectionId:guid}/cards", (HttpContext ctx, Guid collectionId, CollectionsHandler h, CancellationToken ct) =>
                h.ListCardsAsync(ctx, collectionId, ct))
            .WithSummary("List the cards in a collection.");

        group.MapPost("/{collectionId:guid}/cards", (HttpContext ctx, Guid collectionId, AddCardToCollectionRequest body, CollectionsHandler h, CancellationToken ct) =>
                h.AddCardAsync(ctx, collectionId, body, ct))
            .WithSummary("Add a card to a collection (manual, no scan).");

        group.MapDelete("/{collectionId:guid}/cards/{instanceId:guid}", (HttpContext ctx, Guid collectionId, Guid instanceId, CollectionsHandler h, CancellationToken ct) =>
                h.RemoveCardAsync(ctx, collectionId, instanceId, ct))
            .WithSummary("Remove a card from a collection.");

        group.MapPost("/{collectionId:guid}/cards/{instanceId:guid}/move", (HttpContext ctx, Guid collectionId, Guid instanceId, MoveCardRequest body, CollectionsHandler h, CancellationToken ct) =>
                h.MoveCardAsync(ctx, collectionId, instanceId, body, ct))
            .WithSummary("Move a card to another collection.");

        return app;
    }
}
