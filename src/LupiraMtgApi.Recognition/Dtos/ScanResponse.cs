namespace LupiraMtgApi.Recognition.Dtos;

public sealed class ScanResponse
{
    public required Guid ScanId { get; set; }

    public required RecognitionConfidence Confidence { get; set; }

    public required IReadOnlyList<CardCandidateDto> Candidates { get; set; }

    public required ScanDebug Debug { get; set; }
}
