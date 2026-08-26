using LupiraMtgApi.Dtos.Sync;
using LupiraMtgApi.Handlers;

namespace LupiraMtgApi.Endpoints.Admin;

public static class SyncEndpoints
{
    public static IEndpointConventionBuilder MapAdminSync(this IEndpointRouteBuilder app) =>
        app.MapPost("/admin/sync/run", (AdminSyncHandler handler, CancellationToken ct) => handler.RunAsync(ct))
            .WithTags("Admin")
            .WithSummary("Trigger a Scryfall sync run synchronously.")
            .WithDescription(
                """
                Runs the same Scryfall sync the cron-scheduled job runs, in-process, and returns the
                completed report. The first call against an empty database can take several hours
                (downloading and pHashing every printing); subsequent calls only download new printings.

                This endpoint is auth-gated; in production it should be restricted further (admin role)
                once roles are introduced.
                """)
            .Produces<SyncRunResponse>(StatusCodes.Status200OK)
            .WithName("RunSync");
}
