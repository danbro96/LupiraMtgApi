using LupiraMtgApi.Handlers;

namespace LupiraMtgApi.Endpoints.Admin;

public static class SetTypeWeightsEndpoints
{
    public static IEndpointRouteBuilder MapSetTypeWeights(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/admin/set-type-weights")
            .RequireAuthorization()
            .WithTags("Admin");

        group.MapGet("/", (SetTypeWeightHandler h, CancellationToken ct) => h.ListAsync(ct))
            .WithSummary("List set-type weights used by the scan-confidence ranker.")
            .WithDescription(
                """
                The scan ranker biases candidates by their printing's set type — e.g. weighting
                core/expansion above commander/funny so a Lightning Bolt scan prefers the M21
                printing over a Funko Pop one. This endpoint exposes the live values.
                """)
            .Produces<SetTypeWeightListResponse>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status401Unauthorized);

        group.MapPut("/{setType}", (string setType, UpdateSetTypeWeightRequest body, SetTypeWeightHandler h, CancellationToken ct) =>
                h.UpsertAsync(setType, body, ct))
            .WithSummary("Upsert the weight for one set type.")
            .WithDescription(
                """
                Body: `{ weight: number }`. Weight must be finite and non-negative; 1.0 is the
                neutral baseline, < 1 down-weights, > 1 up-weights. New `setType` keys are created
                on the fly so you don't have to pre-seed.

                The change takes effect on the *next* scan; the ranker reads weights per request.
                """)
            .Produces<SetTypeWeightResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status401Unauthorized);

        return app;
    }
}
