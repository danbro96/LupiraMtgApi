using LupiraMtgApi.Handlers;

namespace LupiraMtgApi.Endpoints.Me;

public static class ScanHistoryEndpoints
{
    public static IEndpointRouteBuilder MapScanHistory(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/me/scans")
            .RequireAuthorization()
            .WithTags("Me");

        group.MapGet("/", (
                HttpContext ctx,
                int? take,
                int? skip,
                ScanHistoryHandler h,
                CancellationToken ct) =>
            h.ListAsync(ctx, take, skip, ct))
            .WithSummary("Paginated scan history (newest first).")
            .WithDescription(
                """
                Returns the caller's recent scans with the top match hydrated for each. `take` is
                1–100 (default 25); `skip` for paging. Use `GET /me/scans/{scanId}` for the full
                ScanLog projection (every candidate, OCR zones, set-symbol detection, feedback).
                """)
            .Produces<ScanListResponse>(StatusCodes.Status200OK)
            .WithName("ListScans");

        group.MapGet("/{scanId:guid}", (
                HttpContext ctx,
                Guid scanId,
                ScanHistoryHandler h,
                CancellationToken ct) =>
            h.GetAsync(ctx, scanId, ct))
            .WithSummary("Full detail of one scan.")
            .WithDescription(
                """
                Returns every candidate (hydrated with the printing's metadata + presigned image),
                the OCR zone texts, the set-symbol detection if any, the originally captured image
                (presigned URL, 15-min TTL) when retained, and feedback if submitted. Useful for
                a "why did the scan match this card?" diagnostic UI without needing OpenObserve.
                """)
            .Produces<ScanDetailResponse>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status404NotFound)
            .WithName("GetScan");

        return app;
    }
}
