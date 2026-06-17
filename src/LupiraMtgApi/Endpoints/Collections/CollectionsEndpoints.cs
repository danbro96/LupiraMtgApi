using LupiraMtgApi.Handlers;

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
            .ProducesProblem(StatusCodes.Status400BadRequest)
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
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status404NotFound);

        group.MapDelete("/{collectionId:guid}", (HttpContext ctx, Guid collectionId, CollectionsHandler h, CancellationToken ct) =>
                h.DeleteAsync(ctx, collectionId, ct))
            .WithSummary("Soft-delete a collection.")
            .Produces(StatusCodes.Status204NoContent)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status404NotFound);

        group.MapGet("/{collectionId:guid}/cards", (
                HttpContext ctx,
                Guid collectionId,
                int? take,
                int? skip,
                CollectionsHandler h,
                CancellationToken ct) =>
            h.ListCardsAsync(ctx, collectionId, take, skip, ct))
            .WithSummary("List the cards in a collection.")
            .WithDescription(
                """
                Sorted by card name (ascending). `take` 1–200 (default 50). `skip` for paging.
                Response includes `total`.
                """)
            .Produces<CardInstanceListResponse>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status404NotFound);

        group.MapPost("/{collectionId:guid}/cards", (HttpContext ctx, Guid collectionId, AddCardToCollectionRequest body, CollectionsHandler h, CancellationToken ct) =>
                h.AddCardAsync(ctx, collectionId, body, ct))
            .WithSummary("Add a card to a collection (manual, no scan).")
            .Produces<CardInstanceResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
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
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status404NotFound);

        group.MapPost("/{collectionId:guid}/cards/bulk", (HttpContext ctx, Guid collectionId, BulkAddCardsRequest body, CollectionsHandler h, CancellationToken ct) =>
                h.BulkAddCardsAsync(ctx, collectionId, body, ct))
            .WithSummary("Add many cards to a collection in one call.")
            .WithDescription(
                """
                Body: `{ items: [{ printingId, isFoil?, language?, condition?, count? }] }`.
                Per-item `count` is clamped to 1..50; total instances per call capped at 500.
                Returns the newly-created `CardInstanceResponse` rows in the same order they
                were generated. Validates every `printingId` up front — if any is unknown the
                whole call is rejected with `400`, no partial writes.
                """)
            .Produces<BulkAddCardsResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status404NotFound);

        group.MapPost("/{collectionId:guid}/cards/bulk-delete", (HttpContext ctx, Guid collectionId, BulkRemoveCardsRequest body, CollectionsHandler h, CancellationToken ct) =>
                h.BulkRemoveCardsAsync(ctx, collectionId, body, ct))
            .WithSummary("Remove many cards from a collection in one call.")
            .WithDescription(
                """
                Body: `{ instanceIds: [...] }`. Idempotent on missing ids — returns
                `{ removedCount, missingCount }` so the client can tell the user what landed.
                """)
            .Produces<BulkRemoveCardsResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status404NotFound);

        group.MapPost("/{collectionId:guid}/cards/bulk-move", (HttpContext ctx, Guid collectionId, BulkMoveCardsRequest body, CollectionsHandler h, CancellationToken ct) =>
                h.BulkMoveCardsAsync(ctx, collectionId, body, ct))
            .WithSummary("Move many cards to another collection in one call.")
            .WithDescription(
                """
                Body: `{ instanceIds: [...], toCollectionId }`. Both source and destination
                must exist and belong to the caller. Returns the moved instances (now with
                the destination collection set) plus a count of ids that weren't in the source.
                """)
            .Produces<BulkMoveCardsResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status404NotFound);

        return app;
    }
}
