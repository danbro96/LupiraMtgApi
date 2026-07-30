using System.Diagnostics;
using LupiraMtgApi.Recognition.Application.Pipeline;
using LupiraMtgApi.Recognition.Dtos;

namespace LupiraMtgApi.Recognition.Application;

/// <summary>
/// Runs the recognition pipeline for one uploaded image and shapes the result. Transport concerns —
/// reading the <c>IFormFile</c>, enforcing the size limit, resolving the owner from the bearer token —
/// stay in the host adapter; this service takes the already-buffered bytes.
/// </summary>
public sealed class ScanService
{
    private readonly ScanPipeline _pipeline;
    private readonly ScanScoringOptions _scoring;

    public ScanService(ScanPipeline pipeline, IOptions<ScanScoringOptions> scoring)
    {
        _pipeline = pipeline;
        _scoring = scoring.Value;
    }

    /// <summary>Maximum accepted upload size in bytes; the host enforces it before buffering the body.</summary>
    public long MaxImageBytes => _scoring.MaxImageBytes;

    public async Task<ScanResponse> ScanAsync(byte[] imageBytes, string mediaType, string? ownerId, CancellationToken ct)
    {
        var scanId = Guid.NewGuid();
        var scannedAt = DateTimeOffset.UtcNow;

        var scanStopwatch = Stopwatch.StartNew();
        using var rootSpan = ScanTelemetry.Source.StartActivity("scan");
        rootSpan?.SetTag("scan.id", scanId);
        rootSpan?.SetTag("scan.owner_id", string.IsNullOrEmpty(ownerId) ? "anon" : ownerId);
        rootSpan?.SetTag("scan.image_bytes", imageBytes.Length);
        rootSpan?.SetTag("scan.media_type", mediaType);

        var initialContext = new ScanContext
        {
            ScanId = scanId,
            ScannedAt = scannedAt,
            OwnerId = ownerId,
            OriginalBytes = imageBytes,
            MediaType = mediaType,
            ScanStopwatch = scanStopwatch,
            RootSpan = rootSpan,
        };

        var ctx = await _pipeline.ExecuteAsync(initialContext, ct);

        return BuildResponse(ctx);
    }

    private static ScanResponse BuildResponse(ScanContext ctx) => new()
    {
        ScanId = ctx.ScanId,
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
                NameConfidence = ctx.Zones.NameConfidence,
                TypeLineConfidence = ctx.Zones.TypeLineConfidence,
                RulesTextConfidence = ctx.Zones.RulesTextConfidence,
                PowerToughnessConfidence = ctx.Zones.PowerToughnessConfidence,
                BottomMetadataConfidence = ctx.Zones.BottomMetadataConfidence,
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
