using LupiraMtgApi.Catalog.Dtos.Cards;

namespace LupiraMtgApi.Recognition.Dtos;

public sealed class ScanSummaryResponse
{
    public required Guid Id { get; set; }

    public required DateTimeOffset ScannedAt { get; set; }

    public required RecognitionConfidence Confidence { get; set; }

    /// <summary>The highest-ranked candidate at scan time. Null when no candidates were found.</summary>
    public required CardPrintingResponse? TopMatch { get; set; }

    /// <summary>True iff the user has submitted feedback for this scan via `POST /scans/{id}/feedback`.</summary>
    public required bool HasFeedback { get; set; }

    /// <summary>True when feedback exists AND the user said the top match was wrong.</summary>
    public required bool FeedbackChangedTop { get; set; }
}
