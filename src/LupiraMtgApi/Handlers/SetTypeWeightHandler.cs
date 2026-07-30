using LupiraMtgApi.Catalog.Application;
using Microsoft.AspNetCore.Http.HttpResults;

namespace LupiraMtgApi.Handlers;

/// <summary>
/// Thin transport adapter over <see cref="SetTypeWeightService"/>. Input validation (canonical
/// setType, finite non-negative weight) is a transport concern handled here before the service call.
/// </summary>
public sealed class SetTypeWeightHandler
{
    private readonly SetTypeWeightService _service;

    public SetTypeWeightHandler(SetTypeWeightService service) => _service = service;

    public async Task<Ok<SetTypeWeightListResponse>> ListAsync(CancellationToken ct) =>
        TypedResults.Ok(await _service.ListAsync(ct));

    public async Task<Results<Ok<SetTypeWeightResponse>, ProblemHttpResult>> UpsertAsync(
        string setType,
        UpdateSetTypeWeightRequest request,
        CancellationToken ct)
    {
        var canonical = setType.Trim().ToLowerInvariant();
        if (canonical.Length == 0 || canonical.Length > 32)
        {
            return Problems.BadRequest("setType must be 1..32 characters.");
        }

        if (!double.IsFinite(request.Weight) || request.Weight < 0)
        {
            return Problems.BadRequest("weight must be a finite non-negative number.");
        }

        return TypedResults.Ok(await _service.UpsertAsync(canonical, request.Weight, ct));
    }
}
