namespace LupiraMtgApi.Recognition.Dtos;

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

    public required IReadOnlyList<CardCandidateDto> Candidates { get; set; }

    public required ScanFeedbackInfo? Feedback { get; set; }
}
