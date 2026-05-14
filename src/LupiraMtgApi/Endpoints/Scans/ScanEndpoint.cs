using LupiraMtgApi.Handlers;
using LupiraMtgApi.Models.Scans;
using Microsoft.AspNetCore.Http;

namespace LupiraMtgApi.Endpoints.Scans;

public static class ScanEndpoint
{
    public static IEndpointConventionBuilder MapScan(this IEndpointRouteBuilder app) =>
        app.MapPost("/scans", (
                HttpContext httpContext,
                IFormFile image,
                ScanHandler handler,
                CancellationToken ct) => handler.ScanAsync(httpContext, image, ct))
            .DisableAntiforgery()
            .WithTags("Scans")
            .WithSummary("Recognize a Magic card from a photo.")
            .WithDescription(
                """
                Multipart upload — send the photo as a `multipart/form-data` field named `image`
                (jpeg, ≤4 MB). The backend runs perceptual-hash matching against the local Scryfall
                art-crop index AND OCR via FlorenceApi in parallel, then fuses the two signals into
                a confidence-ranked list of `CardCandidate` objects.

                The `confidence` field (`high|medium|low`) drives the mobile UX: HIGH auto-adds the
                top candidate to the active selection; MEDIUM offers a disambiguation picker; LOW
                falls back to manual lookup.
                """)
            .Accepts<IFormFile>("multipart/form-data")
            .Produces<ScanResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest);
}
