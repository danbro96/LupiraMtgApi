using LupiraMtgApi.Catalog.Application;
using Microsoft.AspNetCore.Http.HttpResults;

namespace LupiraMtgApi.Handlers;

/// <summary>Thin transport adapter over <see cref="SetService"/>.</summary>
public sealed class SetsHandler
{
    private readonly SetService _service;

    public SetsHandler(SetService service) => _service = service;

    public async Task<Ok<SetListResponse>> ListAsync(
        string? setType,
        string? sort,
        string? order,
        int? take,
        int? skip,
        CancellationToken ct) =>
        TypedResults.Ok(await _service.ListAsync(setType, sort, order, take, skip, ct));

    public async Task<Results<Ok<SetDto>, NotFound>> GetByCodeAsync(string code, CancellationToken ct)
    {
        var set = await _service.GetByCodeAsync(code, ct);
        return set is null ? TypedResults.NotFound() : TypedResults.Ok(set);
    }
}
