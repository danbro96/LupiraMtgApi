using LupiraMtgApi.Handlers;

namespace LupiraMtgApi.Endpoints.Sets;

public static class SetsEndpoints
{
    public static IEndpointRouteBuilder MapSets(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/sets")
            .RequireAuthorization()
            .WithTags("Sets");

        group.MapGet("/", (
                string? setType,
                string? sort,
                string? order,
                int? take,
                int? skip,
                SetsHandler handler,
                CancellationToken ct) =>
            handler.ListAsync(setType, sort, order, take, skip, ct))
            .WithSummary("Browse the catalog of Magic sets.")
            .WithDescription(
                """
                Returns paginated set metadata. Use this to power a set picker (the `?set=`
                filter on `GET /cards` takes the same `code` returned here).

                `setType` filters by Scryfall set type (`core`, `expansion`, `masters`,
                `commander`, `funny`, …).
                `sort` ∈ `releasedAt|code|name`, default `releasedAt`.
                `order` ∈ `asc|desc`, default `desc` (newest first).
                `take` 1–200 (default 50). `skip` for paging.
                """)
            .Produces<SetListResponse>(StatusCodes.Status200OK);

        group.MapGet("/{code}", (
                string code,
                SetsHandler handler,
                CancellationToken ct) =>
            handler.GetByCodeAsync(code, ct))
            .WithSummary("Get one set by its lower-case code.")
            .WithDescription("Returns set metadata plus a presigned URL for the set icon (if cached locally).")
            .Produces<SetResponse>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status404NotFound);

        return app;
    }
}
