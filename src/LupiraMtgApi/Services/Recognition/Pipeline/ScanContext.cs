using System.Diagnostics;
using LupiraMtgApi.Models;
using LupiraMtgApi.Models.Scans;
using LupiraMtgApi.Services.Imaging;
using LupiraMtgApi.Services.Ocr;
using LupiraMtgApi.Services.SetSymbol;

namespace LupiraMtgApi.Services.Recognition.Pipeline;

/// <summary>
/// Append-only accumulator passed between scan pipeline steps. Each step reads what
/// previous steps populated and returns a new context with its own outputs added via
/// <see cref="With"/>; no step mutates an earlier step's outputs.
///
/// This is the single shared state object; replaces the dozen-odd local variables
/// that were threaded through the procedural ScanHandler.
/// </summary>
public sealed record ScanContext
{
    /// <summary>Identity + timing for this scan.</summary>
    public required Guid ScanId { get; init; }

    public required DateTimeOffset ScannedAt { get; init; }

    /// <summary>Authenticated owner, when present. Drives image upload + scan log persistence.</summary>
    public Guid? OwnerId { get; init; }

    /// <summary>Original (pre-crop) bytes uploaded by the client.</summary>
    public required byte[] OriginalBytes { get; init; }

    public required string MediaType { get; init; }

    /// <summary>End-to-end stopwatch started at the top of the pipeline; consulted by the persistence/metrics tail steps.</summary>
    public required Stopwatch ScanStopwatch { get; init; }

    /// <summary>Root OpenTelemetry activity for the scan; child spans within steps parent under it.</summary>
    public Activity? RootSpan { get; init; }

    // ---- Step outputs, populated as the pipeline progresses ----

    public string? ImageObjectKey { get; init; }

    public bool ImageUploaded { get; init; }

    public CardCropResult? Preprocessed { get; init; }

    public OcrRegions Regions { get; init; } = OcrRegions.Empty;

    public IReadOnlyList<PHashIndex.PHashHit> PHashHits { get; init; } = Array.Empty<PHashIndex.PHashHit>();

    public long? ImageHash { get; init; }

    public int OcrLatencyMs { get; init; }

    public int PHashLatencyMs { get; init; }

    public SetSymbolMatch? SymbolMatch { get; init; }

    public CardZones Zones { get; init; } = CardZones.Empty;

    public CardZoneScoringResult? ZoneScoring { get; init; }

    public bool RotationRetried { get; init; }

    /// <summary>Per-printing accumulator before final ranking; mutable in-place by the fusion + set-type steps.</summary>
    public Dictionary<string, RankedCandidate> ByPrinting { get; init; } = new(StringComparer.Ordinal);

    /// <summary>Top-N rows after fusion + set-type weighting + sort.</summary>
    public IReadOnlyList<RankedCandidate> TopRanked { get; init; } = Array.Empty<RankedCandidate>();

    /// <summary>Hydrated response candidates, aligned by index with HydratedRows.</summary>
    public IReadOnlyList<CardCandidateResponse> Ranked { get; init; } = Array.Empty<CardCandidateResponse>();

    /// <summary>Source RankedCandidate per emitted Ranked entry; alignment preserved by HydrateStep so ConfidenceStep can derive zone agreement from the matched row.</summary>
    public IReadOnlyList<RankedCandidate> HydratedRows { get; init; } = Array.Empty<RankedCandidate>();

    public RecognitionConfidence Confidence { get; init; } = RecognitionConfidence.Low;
}
