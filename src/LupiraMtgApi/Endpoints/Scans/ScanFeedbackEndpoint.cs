using LupiraMtgApi.Handlers;
using LupiraMtgApi.Models.Scans;

namespace LupiraMtgApi.Endpoints.Scans;

public static class ScanFeedbackEndpoint
{
    public static IEndpointConventionBuilder MapScanFeedback(this IEndpointRouteBuilder app) =>
        app.MapPost("/scans/{scanId:guid}/feedback", (
                HttpContext httpContext,
                Guid scanId,
                ScanFeedbackRequest request,
                ScanFeedbackHandler handler,
                CancellationToken ct) => handler.SubmitAsync(httpContext, scanId, request, ct))
            .WithTags("Scans")
            .WithSummary("Report the actual correct printing for a previous scan.")
            .WithDescription(
                """
                Tells the API which printing the user actually wanted from a scan referenced
                by `scanId` (returned in `POST /scans`). The response reports the 1-based
                `rank` of that printing in the original candidate list, or `null` when the
                printing wasn't in the pool at all.

                The submission is persisted on the scan record and accumulates as training
                data for future ranker work. Submitting a second time overwrites the prior
                feedback — assume the latest is the user's most considered answer.
                """)
            .Produces<ScanFeedbackResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status404NotFound);
}
