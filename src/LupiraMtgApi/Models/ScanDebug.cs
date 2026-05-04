namespace LupiraMtgApi.Models;

public sealed class ScanDebug
{
    public required ScanZoneTexts Zones { get; set; }

    public long? ImagePHash { get; set; }

    public required bool Cropped { get; set; }

    public required double CropConfidence { get; set; }

    public required int CroppedWidth { get; set; }

    public required int CroppedHeight { get; set; }

    public required int OcrRegionCount { get; set; }

    public required int PHashCandidateCount { get; set; }

    public required int OcrCandidateCount { get; set; }

    public required int OcrLatencyMs { get; set; }

    public required int PHashLatencyMs { get; set; }
}

public sealed class ScanZoneTexts
{
    public required string Name { get; set; }

    public required string TypeLine { get; set; }

    public required string RulesText { get; set; }

    public required string PowerToughness { get; set; }

    public required string BottomMetadata { get; set; }
}
