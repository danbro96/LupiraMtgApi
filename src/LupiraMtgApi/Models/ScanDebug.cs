namespace LupiraMtgApi.Models;

public sealed class ScanDebug
{
    public string? OcrText { get; set; }

    public long? ImagePHash { get; set; }

    public required int PHashCandidateCount { get; set; }

    public required int OcrCandidateCount { get; set; }

    public required int OcrLatencyMs { get; set; }

    public required int PHashLatencyMs { get; set; }
}
