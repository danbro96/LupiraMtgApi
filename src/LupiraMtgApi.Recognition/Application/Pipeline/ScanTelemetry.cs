using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace LupiraMtgApi.Recognition.Application.Pipeline;

/// <summary>
/// Shared OpenTelemetry sources for the scan pipeline. ActivitySource picked up by
/// the global <c>AddSource("LupiraMtgApi.*")</c> in Program.cs; Meter picked up by
/// <c>AddMeter("LupiraMtgApi.*")</c>. All step instruments live here so we don't
/// scatter `new ActivitySource(...)` declarations across 11 files.
/// </summary>
internal static class ScanTelemetry
{
    public static readonly ActivitySource Source = new("LupiraMtgApi.Scans");

    public static readonly Meter Meter = new("LupiraMtgApi.Scans");

    // Per-scan outcome metrics — recorded in the tail steps (ConfidenceStep, FusionStep).
    public static readonly Histogram<double> ScanDuration = Meter.CreateHistogram<double>("scan.duration_ms", unit: "ms", description: "End-to-end scan latency");
    public static readonly Histogram<double> OcrDuration = Meter.CreateHistogram<double>("scan.ocr.duration_ms", unit: "ms", description: "OCR latency including any rotation-retry pass");
    public static readonly Histogram<double> PhashDuration = Meter.CreateHistogram<double>("scan.phash.duration_ms", unit: "ms", description: "pHash compute + index search latency");
    public static readonly Histogram<int> OcrRegionCount = Meter.CreateHistogram<int>("scan.ocr.region_count", description: "Number of OCR regions returned by Florence");
    public static readonly Histogram<int> PhashCandidateCount = Meter.CreateHistogram<int>("scan.phash.candidate_count", description: "BK-tree candidates within the hamming cutoff (merged art + full-card)");
    public static readonly Histogram<int> FullPhashCandidateCount = Meter.CreateHistogram<int>("scan.phash.full.candidate_count", description: "Full-card BK-tree hits within the hamming cutoff");
    public static readonly Counter<long> WinningPhashSource = Meter.CreateCounter<long>("scan.phash.winning_source.total", description: "pHash signal carrying the top match: art / full / both / neither");
    public static readonly Histogram<int> ZoneCoverage = Meter.CreateHistogram<int>("scan.zone.coverage", description: "Number of zones with meaningful content (0..5)");
    public static readonly Histogram<double> TopCombined = Meter.CreateHistogram<double>("scan.top.combined_score", description: "Final combined score of the top candidate");
    public static readonly Counter<long> ConfidenceCount = Meter.CreateCounter<long>("scan.confidence.total", description: "Scans by confidence outcome");
    public static readonly Counter<long> RotationRetryCount = Meter.CreateCounter<long>("scan.rotation.retried.total", description: "Scans that ran the alt-rotation OCR pass");
    public static readonly Counter<long> CropFailures = Meter.CreateCounter<long>("scan.crop.failures.total", description: "Crop preprocessor exceptions");
    public static readonly Counter<long> OcrFailures = Meter.CreateCounter<long>("scan.ocr.failures.total", description: "Florence OCR-call exceptions");
    public static readonly Counter<long> PhashFailures = Meter.CreateCounter<long>("scan.phash.failures.total", description: "pHash compute exceptions");
    public static readonly Counter<long> UploadFailures = Meter.CreateCounter<long>("scan.upload.failures.total", description: "MinIO upload exceptions on the scan path");
}
