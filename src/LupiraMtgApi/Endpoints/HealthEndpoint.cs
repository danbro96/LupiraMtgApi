using LupiraMtgApi.Models;
using Microsoft.AspNetCore.Http.HttpResults;
using LupiraMtgApi.Models.Health;
namespace LupiraMtgApi.Endpoints;

public static class HealthEndpoint
{
    public static IEndpointConventionBuilder MapHealthEndpoint(this IEndpointRouteBuilder app) =>
        app.MapGet("/healthz", static Ok<HealthResponse> () => TypedResults.Ok(new HealthResponse { Status = "ok" }))
            .AllowAnonymous()
            .WithTags("Meta")
            .WithSummary("Liveness probe.")
            .WithDescription(
                """
                Returns 200 with `{ "status": "ok" }` as soon as the process is up.
                Used by the TrueNAS / Docker healthcheck. Anonymous — no token required.
                """)
            .Produces<HealthResponse>(StatusCodes.Status200OK);
}
