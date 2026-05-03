namespace LupiraMtgApi.Models;

public sealed class ScanResponse
{
    public required RecognitionConfidence Confidence { get; set; }

    public required IReadOnlyList<CardCandidateResponse> Candidates { get; set; }

    public required ScanDebug Debug { get; set; }
}
