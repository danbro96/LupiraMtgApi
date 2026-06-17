using LupiraMtgApi.Catalog.Infrastructure.Scryfall;
using LupiraMtgApi.Recognition.Application.Pipeline;
using Marten;

namespace LupiraMtgApi.Recognition.Application.Steps;

/// <summary>
/// Persists a <see cref="ScanLogDocument"/> to Marten when the scan has an
/// authenticated owner. Best-effort: a Marten/Postgres failure must not break the
/// scan response. Stores everything a future re-extractor would need to re-process
/// the original image without re-asking the user.
/// </summary>
public sealed class PersistScanLogStep : IScanStep
{
    private readonly IDocumentSession _session;
    private readonly ILogger<PersistScanLogStep> _logger;

    public PersistScanLogStep(IDocumentSession session, ILogger<PersistScanLogStep> logger)
    {
        _session = session;
        _logger = logger;
    }

    public string Name => "persist_log";

    public async Task<ScanContext> ExecuteAsync(ScanContext ctx, CancellationToken ct)
    {
        if (ctx.OwnerId is not Guid ownerId)
        {
            return ctx;
        }

        using var span = ScanTelemetry.Source.StartActivity("persist_log");
        var preprocessed = ctx.Preprocessed;
        if (preprocessed is null)
        {
            return ctx;
        }

        try
        {
            var (supertype, type, subtype) = TypeLineParser.Parse(ctx.Zones.TypeLine);
            var (power, toughness) = SplitPowerToughness(ctx.Zones.PowerToughness);

            var doc = new ScanLogDocument
            {
                Id = ctx.ScanId,
                OwnerId = ownerId,
                ScannedAt = ctx.ScannedAt,
                ImageObjectKey = ctx.ImageUploaded ? ctx.ImageObjectKey : null,
                ImageMediaType = ctx.MediaType,
                ImageBytes = ctx.OriginalBytes.Length,
                ImagePHash = ctx.ImageHash,
                Confidence = ctx.Confidence,
                PHashLatencyMs = ctx.PHashLatencyMs,
                OcrLatencyMs = ctx.OcrLatencyMs,
                IsCropped = preprocessed.IsCropped,
                CropConfidence = preprocessed.CropConfidence,
                CroppedWidth = preprocessed.Width,
                CroppedHeight = preprocessed.Height,
                OcrName = ScanHelpers.NullIfEmpty(ctx.Zones.Name),
                OcrTypeLine = ScanHelpers.NullIfEmpty(ctx.Zones.TypeLine),
                OcrRulesText = ScanHelpers.NullIfEmpty(ctx.Zones.RulesText),
                OcrPowerToughness = ScanHelpers.NullIfEmpty(ctx.Zones.PowerToughness),
                OcrBottomMetadata = ScanHelpers.NullIfEmpty(ctx.Zones.BottomMetadata),
                DetectedSetCode = ctx.SymbolMatch?.SetCode,
                DetectedSetSymbolHamming = ctx.SymbolMatch?.HammingDistance,
                ExtractedCardName = ScanHelpers.NullIfEmpty(ctx.Zones.Name),
                ExtractedSupertype = supertype,
                ExtractedType = ScanHelpers.NullIfEmpty(type),
                ExtractedSubtype = subtype,
                ExtractedRulesText = ScanHelpers.NullIfEmpty(ctx.Zones.RulesText),
                ExtractedPower = power,
                ExtractedToughness = toughness,
                ExtractedBottomLeftMetadata = ScanHelpers.NullIfEmpty(ctx.Zones.BottomMetadata),
                Candidates = ctx.HydratedRows.Select(r => new ScanLogCandidate
                {
                    PrintingId = r.PrintingId,
                    SetCode = r.SetCode,
                    SetType = r.SetType,
                    SetTypeWeight = r.SetTypeWeight,
                    CombinedScore = r.FinalScore,
                    OcrAggregateScore = r.ZoneScores?.AggregateScore ?? 0.0,
                    NameScore = r.ZoneScores?.NameScore ?? 0.0,
                    TypeLineScore = r.ZoneScores?.TypeLineScore ?? 0.0,
                    RulesTextScore = r.ZoneScores?.RulesTextScore ?? 0.0,
                    PowerToughnessScore = r.ZoneScores?.PowerToughnessScore ?? 0.0,
                    BottomMetadataScore = r.ZoneScores?.BottomMetadataScore ?? 0.0,
                    HammingScore = r.HammingScore,
                    HammingDistance = r.HammingDistance,
                    MatchedByPHash = r.HammingDistance.HasValue,
                    MatchedByName = (r.ZoneScores?.NameScore ?? 0.0) > 0,
                }).ToList(),
            };

            _session.Store(doc);
            await _session.SaveChangesAsync(ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to persist scan log {ScanId}", ctx.ScanId);
        }

        return ctx;
    }

    private static (string? Power, string? Toughness) SplitPowerToughness(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return (null, null);
        }

        var trimmed = raw.Trim();
        var slash = trimmed.IndexOf('/');
        if (slash < 0)
        {
            return (null, null);
        }

        var power = trimmed[..slash].Trim();
        var toughness = trimmed[(slash + 1)..].Trim();
        return (
            string.IsNullOrEmpty(power) ? null : power,
            string.IsNullOrEmpty(toughness) ? null : toughness);
    }
}
