namespace LupiraMtgApi.Recognition.Dtos;

public sealed class ScanZoneTexts
{
    public required string Name { get; set; }

    public required string TypeLine { get; set; }

    public required string RulesText { get; set; }

    public required string PowerToughness { get; set; }

    public required string BottomMetadata { get; set; }

    public required double NameConfidence { get; set; }

    public required double TypeLineConfidence { get; set; }

    public required double RulesTextConfidence { get; set; }

    public required double PowerToughnessConfidence { get; set; }

    public required double BottomMetadataConfidence { get; set; }
}
