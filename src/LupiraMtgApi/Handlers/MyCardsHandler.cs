using LupiraMtgApi.Collections.Application;
using Microsoft.AspNetCore.Http.HttpResults;

namespace LupiraMtgApi.Handlers;

/// <summary>Thin transport adapter over <see cref="MyCardsService"/>.</summary>
public sealed class MyCardsHandler
{
    private readonly MyCardsService _service;

    public MyCardsHandler(MyCardsService service) => _service = service;

    public async Task<Results<Ok<CardInstanceListResponse>, UnauthorizedHttpResult>> ListAsync(
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
}
