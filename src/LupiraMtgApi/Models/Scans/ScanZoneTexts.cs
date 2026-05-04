namespace LupiraMtgApi.Models.Scans;

public sealed class ScanZoneTexts
{
    public required string Name { get; set; }

    public required string TypeLine { get; set; }

    public required string RulesText { get; set; }

    public required string PowerToughness { get; set; }

    public required string BottomMetadata { get; set; }
}