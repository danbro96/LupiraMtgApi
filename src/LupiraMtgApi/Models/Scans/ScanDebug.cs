namespace LupiraMtgApi.Models.Scans;

public sealed class ScanDebug
{
    public required ScanZoneTexts Zones { get; set; }

    public ScanSetSymbol? SetSymbol { get; set; }

    public long? ImagePHash { get; set; }

    public required bool Cropped { get; set; }

    public required double CropConfidence { get; set; }

    public required bool CropRotated { get; set; }

    public required int CroppedWidth { get; set; }

    public required int CroppedHeight { get; set; }

    public required int OcrRegionCount { get; set; }

    public required int PHashCandidateCount { get; set; }

    public required int OcrCandidateCount { get; set; }

    public required int OcrLatencyMs { get; set; }

    public required int PHashLatencyMs { get; set; }
}
