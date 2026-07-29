using LupiraMtgApi.Catalog.Infrastructure.Storage;
using LupiraMtgApi.Recognition.Application.Pipeline;

namespace LupiraMtgApi.Recognition.Application.Steps;

/// <summary>
/// Best-effort upload of the original (pre-crop) image bytes to MinIO, so a future smarter extractor can
/// re-process the user's actual capture rather than the cropped derivative. Only fires when the scan has an
/// authenticated owner. A MinIO failure must not break scanning — the outcome is recorded in
/// <see cref="ScanContext.ImageUploaded"/> for the persistence step to consult. Runs sequentially at the head of
/// the pipeline; its ~30-100ms costs little against multi-second OCR latency.
/// </summary>
public sealed class UploadOriginalStep : IScanStep
{
    private readonly IImageStore _images;
    private readonly ILogger<UploadOriginalStep> _logger;

    public UploadOriginalStep(IImageStore images, ILogger<UploadOriginalStep> logger)
    {
        _images = images;
        _logger = logger;
    }

    public string Name => "upload.original";

    public async Task<ScanContext> ExecuteAsync(ScanContext ctx, CancellationToken ct)
    {
        if (ctx.OwnerId is not { Length: > 0 } ownerId)
        {
            return ctx;
        }

        var key = ScanHelpers.BuildScanObjectKey(ownerId, ctx.ScannedAt, ctx.ScanId, ctx.MediaType);

        using var span = ScanTelemetry.Source.StartActivity("upload.original");
        span?.SetTag("upload.object_key", key);
        span?.SetTag("upload.bytes", ctx.OriginalBytes.Length);

        try
        {
            using var ms = new MemoryStream(ctx.OriginalBytes, writable: false);
            await _images.PutAsync(key, ms, ctx.MediaType, ct);
            span?.SetTag("upload.success", true);
            return ctx with { ImageObjectKey = key, ImageUploaded = true };
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to upload scan image for scan {ScanId} to object store at key {ObjectKey}", ctx.ScanId, key);
            span?.SetTag("upload.success", false);
            span?.SetTag("error.type", ex.GetType().Name);
            ScanTelemetry.UploadFailures.Add(1, new KeyValuePair<string, object?>("error.type", ex.GetType().Name));
            return ctx with { ImageObjectKey = key, ImageUploaded = false };
        }
    }
}
