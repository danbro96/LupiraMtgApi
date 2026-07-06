using LupiraMtgApi.Recognition.Application.Pipeline;
using LupiraMtgApi.Recognition.Infrastructure.Imaging;
using LupiraMtgApi.Recognition.Infrastructure.Ocr;
using LupiraMtgApi.Recognition.Infrastructure.SetSymbol;
using System.Diagnostics;

namespace LupiraMtgApi.Recognition.Application.Steps;

/// <summary>
/// Runs the three independent recognition signals in parallel: dual-rotation pHash
/// (art + full-card), Florence OCR regions, and set-symbol detection. Bundled into
/// one step because they share no inputs — splitting would require a ParallelStep
/// abstraction without buying anything.
///
/// Each signal has its own try/catch so a Florence outage doesn't kill pHash and
/// vice-versa; the signal that fails returns empty results, downstream fusion
/// degrades gracefully.
/// </summary>
public sealed class PrimaryRecognitionStep : IScanStep
{
    private readonly ScanPHashRunner _pHash;
    private readonly IOcrService _ocr;
    private readonly SetSymbolDetector _symbolDetector;
    private readonly ILogger<PrimaryRecognitionStep> _logger;

    public PrimaryRecognitionStep(
        ScanPHashRunner pHash,
        IOcrService ocr,
        SetSymbolDetector symbolDetector,
        ILogger<PrimaryRecognitionStep> logger)
    {
        _pHash = pHash;
        _ocr = ocr;
        _symbolDetector = symbolDetector;
        _logger = logger;
    }

    public string Name => "primary_recognition";

    public async Task<ScanContext> ExecuteAsync(ScanContext ctx, CancellationToken ct)
    {
        var preprocessed = ctx.Preprocessed
            ?? throw new InvalidOperationException("PrimaryRecognitionStep requires CropStep to have run first.");

        // First-pass pHash tries both rotations when the cropper had to rotate — the CW
        // default may be wrong, and pHash has no other recovery path (OCR has the
        // rotation retry below to cover its own case).
        var pHashTask = _pHash.RunAsync(preprocessed.Bytes, ctx.ScanId, tryAltRotation: preprocessed.Rotated);
        var ocrTask = RunOcrAsync(preprocessed.Bytes, preprocessed.MediaType, ctx.ScanId, ct);
        var symbolTask = preprocessed.IsCropped
            ? RunSymbolDetectAsync(preprocessed.Bytes, ct)
            : Task.FromResult<SetSymbolMatch?>(null);

        await Task.WhenAll(pHashTask, ocrTask, symbolTask);

        var pHashResult = pHashTask.Result;
        var (regions, ocrLatencyMs) = ocrTask.Result;
        var symbolMatch = symbolTask.Result;

        return ctx with
        {
            ImageHash = pHashResult.Hash,
            PHashHits = pHashResult.Hits,
            PHashLatencyMs = pHashResult.LatencyMs,
            Regions = regions,
            OcrLatencyMs = ocrLatencyMs,
            SymbolMatch = symbolMatch,
        };
    }

    private async Task<(OcrRegions Regions, int LatencyMs)> RunOcrAsync(byte[] imageBytes, string mediaType, Guid scanId, CancellationToken ct)
    {
        using var span = ScanTelemetry.Source.StartActivity("ocr.regions");
        span?.SetTag("ocr.image_bytes", imageBytes.Length);

        var sw = Stopwatch.StartNew();
        try
        {
            var regions = await _ocr.ReadRegionsAsync(imageBytes, mediaType, ct);
            sw.Stop();
            span?.SetTag("ocr.region_count", regions.Regions.Count);
            return (regions, (int) sw.ElapsedMilliseconds);
        }
        catch (Exception ex)
        {
            sw.Stop();
            _logger.LogWarning(ex, "OCR regions call failed for scan {ScanId}; falling back to pHash-only candidates", scanId);
            span?.SetTag("error.type", ex.GetType().Name);
            ScanTelemetry.OcrFailures.Add(1, new KeyValuePair<string, object?>("error.type", ex.GetType().Name));
            return (OcrRegions.Empty, (int) sw.ElapsedMilliseconds);
        }
    }

    private async Task<SetSymbolMatch?> RunSymbolDetectAsync(byte[] bytes, CancellationToken ct)
    {
        using var span = ScanTelemetry.Source.StartActivity("symbol.detect");
        var match = await _symbolDetector.DetectAsync(bytes, ct);
        span?.SetTag("symbol.matched", match is not null);
        if (match is not null)
        {
            span?.SetTag("symbol.set_code", match.SetCode);
            span?.SetTag("symbol.hamming", match.HammingDistance);
            span?.SetTag("symbol.score", match.Score);
        }

        return match;
    }
}
