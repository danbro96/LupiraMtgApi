namespace LupiraMtgApi.Recognition.Dtos;

public sealed class ScanFeedbackInfo
{
    public required string CorrectPrintingId { get; set; }

    /// <summary>1-based rank of the correct printing among the candidates; null if it wasn't in the candidate pool at all.</summary>
    public required int? CorrectPrintingRank { get; set; }

    public required DateTimeOffset At { get; set; }
}
