using LupiraMtgApi.Collections.Application;
using Microsoft.AspNetCore.Http.HttpResults;

namespace LupiraMtgApi.Handlers;

/// <summary>
/// Thin transport adapter over <see cref="CollectionsService"/>. Resolves the owner from the bearer
/// token (401 when absent) and maps the service's <see cref="Op{T}"/> / nullable / bool results onto
/// the HTTP shapes the mobile client expects.
/// </summary>
public sealed class CollectionsHandler
{
    private readonly CollectionsService _service;

    public CollectionsHandler(CollectionsService service) => _service = service;

    public async Task<Results<Ok<CollectionListResponse>, UnauthorizedHttpResult>> ListAsync(
        HttpContext httpContext,
        CancellationToken ct)
    {
        if (!httpContext.TryGetOwnerId(out var ownerId))
        {
            return TypedResults.Unauthorized();
        }

        return TypedResults.Ok(await _service.ListAsync(ownerId, ct));
    }

    public async Task<Results<Ok<CollectionResponse>, ProblemHttpResult, UnauthorizedHttpResult>> CreateAsync(
        HttpContext httpContext,
        CreateCollectionRequest request,
        CancellationToken ct)
    {
        if (!httpContext.TryGetOwnerId(out var ownerId))
        {
            return TypedResults.Unauthorized();
        }

        var op = await _service.CreateAsync(ownerId, request, ct);
        return op.Outcome == OpOutcome.Invalid
            ? Problems.BadRequest(op.Error ?? "Invalid request")
            : TypedResults.Ok(op.Value!);
    }

    public async Task<Results<Ok<CollectionDetailResponse>, NotFound, UnauthorizedHttpResult>> GetAsync(
        HttpContext httpContext,
        Guid collectionId,
        CancellationToken ct)
    {
        if (!httpContext.TryGetOwnerId(out var ownerId))
        {
            return TypedResults.Unauthorized();
        }

        var detail = await _service.GetAsync(ownerId, collectionId, ct);
        return detail is null ? TypedResults.NotFound() : TypedResults.Ok(detail);
    }

    public async Task<Results<Ok<CollectionResponse>, NotFound, ProblemHttpResult, UnauthorizedHttpResult>> RenameAsync(
        HttpContext httpContext,
        Guid collectionId,
        RenameCollectionRequest request,
        CancellationToken ct)
    {
        if (!httpContext.TryGetOwnerId(out var ownerId))
        {
            return TypedResults.Unauthorized();
        }

        return MapOp(await _service.RenameAsync(ownerId, collectionId, request, ct));
    }

    public async Task<Results<NoContent, NotFound, UnauthorizedHttpResult>> DeleteAsync(
        HttpContext httpContext,
        Guid collectionId,
        CancellationToken ct)
    {
        if (!httpContext.TryGetOwnerId(out var ownerId))
        {
            return TypedResults.Unauthorized();
        }

        return await _service.DeleteAsync(ownerId, collectionId, ct)
            ? TypedResults.NoContent()
            : TypedResults.NotFound();
    }

    public async Task<Results<Ok<CardInstanceListResponse>, NotFound, UnauthorizedHttpResult>> ListCardsAsync(
        HttpContext httpContext,
        Guid collectionId,
        int? take,
        int? skip,
        CancellationToken ct)
    {
        if (!httpContext.TryGetOwnerId(out var ownerId))
        {
            return TypedResults.Unauthorized();
        }

        var list = await _service.ListCardsAsync(ownerId, collectionId, take, skip, ct);
        return list is null ? TypedResults.NotFound() : TypedResults.Ok(list);
    }

    public async Task<Results<Ok<CardInstanceResponse>, NotFound, ProblemHttpResult, UnauthorizedHttpResult>> AddCardAsync(
        HttpContext httpContext,
        Guid collectionId,
        AddCardToCollectionRequest request,
        CancellationToken ct)
    {
        if (!httpContext.TryGetOwnerId(out var ownerId))
        {
            return TypedResults.Unauthorized();
        }

        return MapOp(await _service.AddCardAsync(ownerId, collectionId, request, ct));
    }

    public async Task<Results<NoContent, NotFound, UnauthorizedHttpResult>> RemoveCardAsync(
        HttpContext httpContext,
        Guid collectionId,
        Guid instanceId,
        CancellationToken ct)
    {
        if (!httpContext.TryGetOwnerId(out var ownerId))
        {
            return TypedResults.Unauthorized();
        }

        return await _service.RemoveCardAsync(ownerId, collectionId, instanceId, ct)
            ? TypedResults.NoContent()
            : TypedResults.NotFound();
    }

    public async Task<Results<Ok<CardInstanceResponse>, NotFound, ProblemHttpResult, UnauthorizedHttpResult>> MoveCardAsync(
        HttpContext httpContext,
        Guid collectionId,
        Guid instanceId,
        MoveCardRequest request,
        CancellationToken ct)
    {
        if (!httpContext.TryGetOwnerId(out var ownerId))
        {
            return TypedResults.Unauthorized();
        }

        return MapOp(await _service.MoveCardAsync(ownerId, collectionId, instanceId, request, ct));
    }

    public async Task<Results<Ok<BulkAddCardsResponse>, NotFound, ProblemHttpResult, UnauthorizedHttpResult>> BulkAddCardsAsync(
        HttpContext httpContext,
        Guid collectionId,
        BulkAddCardsRequest request,
        CancellationToken ct)
    {
        if (!httpContext.TryGetOwnerId(out var ownerId))
        {
            return TypedResults.Unauthorized();
        }

        return MapOp(await _service.BulkAddCardsAsync(ownerId, collectionId, request, ct));
    }

    public async Task<Results<Ok<BulkRemoveCardsResponse>, NotFound, ProblemHttpResult, UnauthorizedHttpResult>> BulkRemoveCardsAsync(
        HttpContext httpContext,
        Guid collectionId,
        BulkRemoveCardsRequest request,
        CancellationToken ct)
    {
        if (!httpContext.TryGetOwnerId(out var ownerId))
        {
            return TypedResults.Unauthorized();
        }

        return MapOp(await _service.BulkRemoveCardsAsync(ownerId, collectionId, request, ct));
    }

    public async Task<Results<Ok<BulkMoveCardsResponse>, NotFound, ProblemHttpResult, UnauthorizedHttpResult>> BulkMoveCardsAsync(
        HttpContext httpContext,
        Guid collectionId,
        BulkMoveCardsRequest request,
        CancellationToken ct)
    {
        if (!httpContext.TryGetOwnerId(out var ownerId))
        {
            return TypedResults.Unauthorized();
        }

        return MapOp(await _service.BulkMoveCardsAsync(ownerId, collectionId, request, ct));
    }

    private static Results<Ok<T>, NotFound, ProblemHttpResult, UnauthorizedHttpResult> MapOp<T>(Op<T> op)
        where T : class => op.Outcome switch
        {
            OpOutcome.NotFound => TypedResults.NotFound(),
            OpOutcome.Invalid => Problems.BadRequest(op.Error ?? "Invalid request"),
            OpOutcome.Conflict => Problems.Conflict(op.Error ?? "Conflict"),
            _ => TypedResults.Ok(op.Value!),
        };
}
