using LupiraMtgApi.Recognition.Application;
using Microsoft.AspNetCore.Http.HttpResults;

namespace LupiraMtgApi.Handlers;

/// <summary>
/// Thin transport adapter over <see cref="ScanService"/>: validates the upload, buffers the bytes,
/// resolves the (optional) owner from the bearer token, and delegates recognition to the service.
/// </summary>
public sealed class ScanHandler
{
    private readonly ScanService _service;

    public ScanHandler(ScanService service) => _service = service;

    public async Task<Results<Ok<ScanResponse>, ProblemHttpResult>> ScanAsync(
        HttpContext httpContext,
        IFormFile image,
        CancellationToken ct)
    {
        if (image is null || image.Length == 0)
        {
            return Problems.BadRequest("Image file is required.");
        }

        if (image.Length > _service.MaxImageBytes)
        {
            return Problems.BadRequest($"Image is too large; max {_service.MaxImageBytes} bytes.");
        }

        byte[] imageBytes;
        await using (var ms = new MemoryStream(capacity: (int) image.Length))
        {
            await image.CopyToAsync(ms, ct);
            imageBytes = ms.ToArray();
        }

        var mediaType = string.IsNullOrEmpty(image.ContentType) ? "image/jpeg" : image.ContentType;
        Guid? ownerId = httpContext.TryGetOwnerId(out var oid) ? oid : null;

        var response = await _service.ScanAsync(imageBytes, mediaType, ownerId, ct);
        return TypedResults.Ok(response);
    }
}
