using LupiraMtgApi.Recognition.Application;
using Microsoft.AspNetCore.Http.HttpResults;

namespace LupiraMtgApi.Handlers;

/// <summary>Thin transport adapter over <see cref="ScanFeedbackService"/>.</summary>
public sealed class ScanFeedbackHandler
{
    private readonly ScanFeedbackService _service;

    public ScanFeedbackHandler(ScanFeedbackService service) => _service = service;

    public async Task<Results<Ok<ScanFeedbackResponse>, NotFound, ProblemHttpResult, UnauthorizedHttpResult>> SubmitAsync(
        HttpContext httpContext,
        Guid scanId,
        ScanFeedbackRequest request,
        CancellationToken ct)
    {
        if (!httpContext.TryGetOwnerId(out var ownerId))
        {
            return TypedResults.Unauthorized();
        }

        if (string.IsNullOrWhiteSpace(request.CorrectPrintingId))
        {
            return Problems.BadRequest("correctPrintingId is required.");
        }

        var result = await _service.SubmitAsync(ownerId, scanId, request.CorrectPrintingId, ct);
        return result.Status switch
        {
            ScanFeedbackStatus.ScanNotFound => TypedResults.NotFound(),
            ScanFeedbackStatus.UnknownPrinting => Problems.BadRequest("Unknown printing id."),
            _ => TypedResults.Ok(result.Response!),
        };
    }
}
