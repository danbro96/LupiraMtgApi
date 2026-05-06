using LupiraMtgApi.Models;
using LupiraMtgApi.Models.Scans;
using LupiraMtgApi.Services.Scryfall;
namespace LupiraMtgApi.Domain.ScanLog;

public sealed class ScanLogDocument
{
    public Guid Id { get; set; }

    public required Guid OwnerId { get; set; }

    public DateTimeOffset ScannedAt { get; set; }

    public string? ImageObjectKey { get; set; }

    public required string ImageMediaType { get; set; }

    public int ImageBytes { get; set; }

    public long? ImagePHash { get; set; }

    public RecognitionConfidence Confidence { get; set; }

    public int PHashLatencyMs { get; set; }

    public int OcrLatencyMs { get; set; }

    public bool IsCropped { get; set; }

    public double CropConfidence { get; set; }

    public int CroppedWidth { get; set; }

    public int CroppedHeight { get; set; }

    public string? OcrName { get; set; }

    public string? OcrTypeLine { get; set; }

    public string? OcrRulesText { get; set; }

    public string? OcrPowerToughness { get; set; }

    public string? OcrBottomMetadata { get; set; }

    public string? DetectedSetCode { get; set; }

    public int? DetectedSetSymbolHamming { get; set; }

    public List<ScanLogCandidate> Candidates { get; set; } = new();

    // Future structured-extraction fields. Names mirror CardPrinting columns so a
    // re-extractor can compare against canonical values directly. Populated today
    // from raw OCR zones via TypeLineParser; richer extractors will overwrite.
    public string? ExtractedCardName { get; set; }

    public string? ExtractedSupertype { get; set; }

    public string? ExtractedType { get; set; }

    public string? ExtractedSubtype { get; set; }

    public string? ExtractedRulesText { get; set; }

    public string? ExtractedPower { get; set; }

    public string? ExtractedToughness { get; set; }

    public string? ExtractedBottomLeftMetadata { get; set; }

    /// <summary>Printing the user told us was actually correct via the feedback endpoint. Null until feedback is submitted.</summary>
    public string? FeedbackCorrectPrintingId { get; set; }

    /// <summary>1-based rank of <see cref="FeedbackCorrectPrintingId"/> within <see cref="Candidates"/>; null when the correct printing wasn't in the candidate pool at all.</summary>
    public int? FeedbackCorrectPrintingRank { get; set; }

    public DateTimeOffset? FeedbackAt { get; set; }
}

public sealed class ScanLogCandidate
{
    public required string PrintingId { get; set; }

    public required string SetCode { get; set; }

    public string? SetType { get; set; }

    public double SetTypeWeight { get; set; }

    public double CombinedScore { get; set; }

    public double OcrAggregateScore { get; set; }

    public double NameScore { get; set; }

    public double TypeLineScore { get; set; }

    public double RulesTextScore { get; set; }

    public double PowerToughnessScore { get; set; }

    public double BottomMetadataScore { get; set; }

    public double HammingScore { get; set; }

    public int? HammingDistance { get; set; }

    public bool MatchedByPHash { get; set; }

    public bool MatchedByName { get; set; }
}
