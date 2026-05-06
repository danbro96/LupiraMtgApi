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
            .WithSummary("List the caller's collections.")
            .Produces<CollectionListResponse>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status401Unauthorized);

        group.MapPost("/", (HttpContext ctx, CreateCollectionRequest body, CollectionsHandler h, CancellationToken ct) =>
                h.CreateAsync(ctx, body, ct))
            .WithSummary("Create a new collection.")
            .Produces<CollectionResponse>(StatusCodes.Status200OK)
            .Produces<string>(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status401Unauthorized);

        group.MapGet("/{collectionId:guid}", (HttpContext ctx, Guid collectionId, CollectionsHandler h, CancellationToken ct) =>
                h.GetAsync(ctx, collectionId, ct))
            .WithSummary("Get a collection with its cards.")
            .Produces<CollectionDetailResponse>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status404NotFound);

        group.MapPatch("/{collectionId:guid}", (HttpContext ctx, Guid collectionId, RenameCollectionRequest body, CollectionsHandler h, CancellationToken ct) =>
                h.RenameAsync(ctx, collectionId, body, ct))
            .WithSummary("Rename a collection.")
            .Produces<CollectionResponse>(StatusCodes.Status200OK)
            .Produces<string>(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status404NotFound);

        group.MapDelete("/{collectionId:guid}", (HttpContext ctx, Guid collectionId, CollectionsHandler h, CancellationToken ct) =>
                h.DeleteAsync(ctx, collectionId, ct))
            .WithSummary("Soft-delete a collection.")
            .Produces(StatusCodes.Status204NoContent)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status404NotFound);

        group.MapGet("/{collectionId:guid}/cards", (HttpContext ctx, Guid collectionId, CollectionsHandler h, CancellationToken ct) =>
                h.ListCardsAsync(ctx, collectionId, ct))
            .WithSummary("List the cards in a collection.")
            .Produces<CardListResponse>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status404NotFound);

        group.MapPost("/{collectionId:guid}/cards", (HttpContext ctx, Guid collectionId, AddCardToCollectionRequest body, CollectionsHandler h, CancellationToken ct) =>
                h.AddCardAsync(ctx, collectionId, body, ct))
            .WithSummary("Add a card to a collection (manual, no scan).")
            .Produces<CardInstanceResponse>(StatusCodes.Status200OK)
            .Produces<string>(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status404NotFound);

        group.MapDelete("/{collectionId:guid}/cards/{instanceId:guid}", (HttpContext ctx, Guid collectionId, Guid instanceId, CollectionsHandler h, CancellationToken ct) =>
                h.RemoveCardAsync(ctx, collectionId, instanceId, ct))
            .WithSummary("Remove a card from a collection.")
            .Produces(StatusCodes.Status204NoContent)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status404NotFound);

        group.MapPost("/{collectionId:guid}/cards/{instanceId:guid}/move", (HttpContext ctx, Guid collectionId, Guid instanceId, MoveCardRequest body, CollectionsHandler h, CancellationToken ct) =>
                h.MoveCardAsync(ctx, collectionId, instanceId, body, ct))
            .WithSummary("Move a card to another collection.")
            .Produces<CardInstanceResponse>(StatusCodes.Status200OK)
            .Produces<string>(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status404NotFound);

        return app;
    }
}
