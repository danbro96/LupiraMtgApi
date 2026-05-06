using System.Diagnostics;
using LupiraMtgApi.Models;
using LupiraMtgApi.Models.Scans;
using LupiraMtgApi.Services.Recognition;
using LupiraMtgApi.Services.Recognition.Pipeline;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.Extensions.Options;

namespace LupiraMtgApi.Handlers;

/// <summary>
/// Thin orchestrator. Validates the upload, builds the initial <see cref="ScanContext"/>,
/// delegates to <see cref="ScanPipeline"/> for the actual recognition work, then shapes
/// the final context into a <see cref="ScanResponse"/>. All the imaging / matching /
/// scoring / persistence logic lives in step classes under
/// <c>Services/Recognition/Steps/</c>; this handler doesn't know how recognition works.
/// </summary>
public sealed class ScanHandler
{
    private readonly ScanPipeline _pipeline;
    private readonly ScanScoringOptions _scoring;

    public ScanHandler(ScanPipeline pipeline, IOptions<ScanScoringOptions> scoring)
    {
        _pipeline = pipeline;
        _scoring = scoring.Value;
    }

    public async Task<Results<Ok<ScanResponse>, BadRequest<string>>> ScanAsync(
        HttpContext httpContext,
        IFormFile image,
        CancellationToken ct)
    {
        if (image is null || image.Length == 0)
        {
            return TypedResults.BadRequest("Image file is required.");
        }

        if (image.Length > _scoring.MaxImageBytes)
        {
            return TypedResults.BadRequest($"Image is too large; max {_scoring.MaxImageBytes} bytes.");
        }

        byte[] imageBytes;
        await using (var ms = new MemoryStream(capacity: (int) image.Length))
        {
            await image.CopyToAsync(ms, ct);
            imageBytes = ms.ToArray();
        }

        var inputMediaType = string.IsNullOrEmpty(image.ContentType) ? "image/jpeg" : image.ContentType;
        var scanId = Guid.NewGuid();
        var scannedAt = DateTimeOffset.UtcNow;

        var hasOwner = httpContext.TryGetOwnerId(out var ownerId);

        var scanStopwatch = Stopwatch.StartNew();
        using var rootSpan = ScanTelemetry.Source.StartActivity("scan");
        rootSpan?.SetTag("scan.id", scanId);
        rootSpan?.SetTag("scan.owner_id", hasOwner ? ownerId.ToString() : "anon");
        rootSpan?.SetTag("scan.image_bytes", imageBytes.Length);
        rootSpan?.SetTag("scan.media_type", inputMediaType);

        var initialContext = new ScanContext
        {
            ScanId = scanId,
            ScannedAt = scannedAt,
            OwnerId = hasOwner ? ownerId : null,
            OriginalBytes = imageBytes,
            MediaType = inputMediaType,
            ScanStopwatch = scanStopwatch,
            RootSpan = rootSpan,
        };

        var ctx = await _pipeline.ExecuteAsync(initialContext, ct);

        return TypedResults.Ok(BuildResponse(ctx));
    }

    private static ScanResponse BuildResponse(ScanContext ctx) => new()
    {
        Confidence = ctx.Confidence,
        Candidates = ctx.Ranked,
        Debug = new ScanDebug
        {
            Zones = new ScanZoneTexts
            {
                Name = ctx.Zones.Name,
                TypeLine = ctx.Zones.TypeLine,
                RulesText = ctx.Zones.RulesText,
                PowerToughness = ctx.Zones.PowerToughness,
                BottomMetadata = ctx.Zones.BottomMetadata,
            },
            SetSymbol = ctx.SymbolMatch is null ? null : new ScanSetSymbol
            {
                SetCode = ctx.SymbolMatch.SetCode,
                HammingDistance = ctx.SymbolMatch.HammingDistance,
                Score = ctx.SymbolMatch.Score,
            },
            ImagePHash = ctx.ImageHash,
            IsCropped = ctx.Preprocessed?.IsCropped ?? false,
            CropConfidence = ctx.Preprocessed?.CropConfidence ?? 0.0,
            CropRotated = ctx.Preprocessed?.Rotated ?? false,
            RotationRetried = ctx.RotationRetried,
            CroppedWidth = ctx.Preprocessed?.Width ?? 0,
            CroppedHeight = ctx.Preprocessed?.Height ?? 0,
            OcrRegionCount = ctx.Regions.Regions.Count,
            PHashCandidateCount = ctx.PHashHits.Count,
            OcrCandidateCount = ctx.ZoneScoring?.ByPrinting.Count ?? 0,
            OcrLatencyMs = ctx.OcrLatencyMs,
            PHashLatencyMs = ctx.PHashLatencyMs,
        },
    };
}
