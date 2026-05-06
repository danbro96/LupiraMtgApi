using LupiraMtgApi.Services.Recognition.Pipeline;

namespace LupiraMtgApi.Services.Recognition.Steps;

/// <summary>
/// Maps OCR regions to logical card zones (Name, TypeLine, RulesText, P/T,
/// BottomMetadata) using fixed proportional bands when the cropper produced a
/// rotated portrait, or relative ordering when not. Pure delegation to
/// <see cref="CardZoneClassifier"/>; behaves as no-op when image dimensions are
/// missing (defensive against the crop fallback path).
/// </summary>
public sealed class ZoneClassifyStep : IScanStep
{
    private readonly CardZoneClassifier _classifier;

    public ZoneClassifyStep(CardZoneClassifier classifier)
    {
        _classifier = classifier;
    }

    public string Name => "zone.classify";

    public Task<ScanContext> ExecuteAsync(ScanContext ctx, CancellationToken ct)
    {
        using var span = ScanTelemetry.Source.StartActivity("zone.classify");
        var preprocessed = ctx.Preprocessed
            ?? throw new InvalidOperationException("ZoneClassifyStep requires CropStep to have run first.");

        var zones = preprocessed.Width > 0 && preprocessed.Height > 0
            ? _classifier.Classify(ctx.Regions, preprocessed.Width, preprocessed.Height, preprocessed.IsCropped)
            : CardZones.Empty;

        span?.SetTag("zone.coverage_score", ScanHelpers.ZoneCoverageScore(zones));
        return Task.FromResult(ctx with { Zones = zones });
    }
}
