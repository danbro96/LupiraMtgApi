using LupiraMtgApi.Collections.Application;
using LupiraMtgApi.Http;
using Microsoft.AspNetCore.Http.HttpResults;

namespace LupiraMtgApi.Handlers;

/// <summary>Thin transport adapter over <see cref="SelectionsService"/>.</summary>
public sealed class SelectionsHandler
{
    private readonly SelectionsService _service;

    public SelectionsHandler(SelectionsService service) => _service = service;

    public async Task<Results<Ok<SelectionResponse>, UnauthorizedHttpResult>> CreateAsync(
        HttpContext httpContext,
        CancellationToken ct)
    {
        if (!httpContext.TryGetOwnerId(out var ownerId))
        {
            return TypedResults.Unauthorized();
        }

        return TypedResults.Ok(await _service.CreateAsync(ownerId, ct));
    }

    public async Task<Results<Ok<SelectionResponse>, NotFound, UnauthorizedHttpResult>> GetAsync(
        HttpContext httpContext,
        Guid selectionId,
        CancellationToken ct)
    {
        if (!httpContext.TryGetOwnerId(out var ownerId))
        {
            return TypedResults.Unauthorized();
        }

        var selection = await _service.GetAsync(ownerId, selectionId, ct);
        return selection is null ? TypedResults.NotFound() : TypedResults.Ok(selection);
    }

    public async Task<Results<Ok<SelectionEntryResponse>, NotFound, ProblemHttpResult, UnauthorizedHttpResult>> AddCardAsync(
        HttpContext httpContext,
        Guid selectionId,
        AddSelectionEntryRequest request,
        CancellationToken ct)
    {
        if (!httpContext.TryGetOwnerId(out var ownerId))
        {
            return TypedResults.Unauthorized();
        }

        var op = await _service.AddCardAsync(ownerId, selectionId, request, ct);
        return op.Outcome switch
        {
            OpOutcome.NotFound => TypedResults.NotFound(),
            OpOutcome.Invalid => Problems.BadRequest(op.Error ?? "Invalid request"),
            OpOutcome.Conflict => Problems.Conflict(op.Error ?? "Conflict"),
            _ => TypedResults.Ok(op.Value!),
        };
    }

    public async Task<Results<NoContent, NotFound, UnauthorizedHttpResult>> RemoveCardAsync(
        HttpContext httpContext,
        Guid selectionId,
        Guid instanceId,
        CancellationToken ct)
    {
        if (!httpContext.TryGetOwnerId(out var ownerId))
        {
            return TypedResults.Unauthorized();
        }

        return await _service.RemoveCardAsync(ownerId, selectionId, instanceId, ct)
            ? TypedResults.NoContent()
            : TypedResults.NotFound();
    }

    public async Task<Results<Ok<CommitSelectionResponse>, NotFound, ProblemHttpResult, UnauthorizedHttpResult>> CommitAsync(
        HttpContext httpContext,
        Guid selectionId,
        CommitSelectionRequest request,
        CancellationToken ct)
    {
        if (!httpContext.TryGetOwnerId(out var ownerId))
        {
            return TypedResults.Unauthorized();
        }

        var op = await _service.CommitAsync(ownerId, selectionId, request, ct);
        return op.Outcome switch
        {
            OpOutcome.NotFound => TypedResults.NotFound(),
            OpOutcome.Invalid => Problems.BadRequest(op.Error ?? "Invalid request"),
            OpOutcome.Conflict => Problems.Conflict(op.Error ?? "Conflict"),
            _ => TypedResults.Ok(op.Value!),
        };
    }
}
