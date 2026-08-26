using LupiraMtgApi.Http;
using LupiraMtgApi.Recognition.Application;
using Microsoft.AspNetCore.Http.HttpResults;

namespace LupiraMtgApi.Handlers;

/// <summary>Thin transport adapter over <see cref="ScanHistoryService"/>.</summary>
public sealed class ScanHistoryHandler
{
    private readonly ScanHistoryService _service;

    public ScanHistoryHandler(ScanHistoryService service) => _service = service;

    public async Task<Results<Ok<ScanListResponse>, UnauthorizedHttpResult>> ListAsync(
        HttpContext httpContext,
        int? take,
        int? skip,
        CancellationToken ct)
    {
        if (!httpContext.TryGetOwnerId(out var ownerId))
        {
            return TypedResults.Unauthorized();
        }

        return TypedResults.Ok(await _service.ListAsync(ownerId, take, skip, ct));
    }

    public async Task<Results<Ok<ScanDetailResponse>, NotFound, UnauthorizedHttpResult>> GetAsync(
        HttpContext httpContext,
        Guid scanId,
        CancellationToken ct)
    {
        if (!httpContext.TryGetOwnerId(out var ownerId))
        {
            return TypedResults.Unauthorized();
        }

        var detail = await _service.GetAsync(ownerId, scanId, ct);
        return detail is null ? TypedResults.NotFound() : TypedResults.Ok(detail);
    }
}
