namespace LupiraMtgApi.Recognition.Dtos;

public sealed class ScanFeedbackResponse
{
    public required Guid ScanId { get; set; }

    public required string CorrectPrintingId { get; set; }

    /// <summary>1-based rank of the correct printing in the original candidate list. Null when the printing wasn't in the candidate pool.</summary>
    public required int? Rank { get; set; }

    /// <summary>Size of the original candidate pool that was searched.</summary>
    public required int CandidateCount { get; set; }
}
