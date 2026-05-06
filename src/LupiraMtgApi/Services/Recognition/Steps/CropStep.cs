using LupiraMtgApi.Services.Imaging;
using LupiraMtgApi.Services.Recognition.Pipeline;

namespace LupiraMtgApi.Services.Recognition.Steps;

/// <summary>
/// Runs <see cref="CardCropService.PreprocessAsync"/>; on failure, falls back to a
/// non-cropped CardCropResult populated with the native image dimensions (via
/// Image.Identify, no full decode) so downstream zone classification still has
/// width/height to work with. The fallback path was a real bug pre-fix B1: a thrown
/// exception used to leave Width=0 and silently disabled OCR scoring.
/// </summary>
public sealed class CropStep : IScanStep
{
    private readonly CardCropService _crop;
    private readonly ILogger<CropStep> _logger;

    public CropStep(CardCropService crop, ILogger<CropStep> logger)
    {
        _crop = crop;
        _logger = logger;
    }

    public string Name => "crop.preprocess";

    public async Task<ScanContext> ExecuteAsync(ScanContext ctx, CancellationToken ct)
    {
        using var span = ScanTelemetry.Source.StartActivity("crop.preprocess");
        try
        {
            var preprocessed = await _crop.PreprocessAsync(ctx.OriginalBytes, ctx.MediaType, ct);
            span?.SetTag("crop.success", true);
            span?.SetTag("crop.cropped", preprocessed.IsCropped);
            span?.SetTag("crop.confidence", preprocessed.CropConfidence);
            span?.SetTag("crop.rotated", preprocessed.Rotated);
            span?.SetTag("crop.width", preprocessed.Width);
            span?.SetTag("crop.height", preprocessed.Height);
            return ctx with { Preprocessed = preprocessed };
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Card crop preprocessing failed for scan {ScanId}; continuing with original image", ctx.ScanId);
            span?.SetTag("crop.success", false);
            span?.SetTag("error.type", ex.GetType().Name);
            ScanTelemetry.CropFailures.Add(1, new KeyValuePair<string, object?>("error.type", ex.GetType().Name));

            var (fallbackW, fallbackH) = ScanHelpers.ProbeImageSize(ctx.OriginalBytes);
            return ctx with
            {
                Preprocessed = new CardCropResult
                {
                    Bytes = ctx.OriginalBytes,
                    MediaType = ctx.MediaType,
                    IsCropped = false,
                    CropConfidence = 0.0,
                    Width = fallbackW,
                    Height = fallbackH,
                },
            };
        }
    }
}
