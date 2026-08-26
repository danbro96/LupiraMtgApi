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

public sealed class ScanListResponse
{
    public required IReadOnlyList<ScanSummaryResponse> Results { get; set; }

    public required int Total { get; set; }
}

public sealed class ScanFeedbackInfo
{
    public required string CorrectPrintingId { get; set; }

    /// <summary>1-based rank of the correct printing among the candidates; null if it wasn't in the candidate pool at all.</summary>
    public required int? CorrectPrintingRank { get; set; }

    public required DateTimeOffset At { get; set; }
}

public sealed class ScanDetailResponse
{
    public required Guid Id { get; set; }

    public required DateTimeOffset ScannedAt { get; set; }

    public required RecognitionConfidence Confidence { get; set; }

    /// <summary>Presigned URL for the originally captured image (15-min TTL). Null if the image was not retained.</summary>
    public required string? ImageUrl { get; set; }

    public required string? ImageMediaType { get; set; }

    public required ScanZoneTexts OcrZones { get; set; }

    public required ScanSetSymbol? SetSymbol { get; set; }

    public required IReadOnlyList<CardCandidateResponse> Candidates { get; set; }

    public required ScanFeedbackInfo? Feedback { get; set; }
}
