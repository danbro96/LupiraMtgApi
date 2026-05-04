namespace LupiraMtgApi.Services.Recognition;

public sealed class ScanScoringOptions
{
    public double PHashWeight { get; set; } = 0.45;

    public double OcrWeight { get; set; } = 0.55;

    public double NameWeight { get; set; } = 0.40;

    public double TypeLineWeight { get; set; } = 0.10;

    public double RulesTextWeight { get; set; } = 0.20;

    public double PowerToughnessWeight { get; set; } = 0.10;

    public double BottomMetadataWeight { get; set; } = 0.20;

    public double NameCutoff { get; set; } = 0.30;

    public double TypeLineCutoff { get; set; } = 0.40;

    public double RulesTextCutoff { get; set; } = 0.30;

    public int NameTopK { get; set; } = 25;

    public int TypeLineTopK { get; set; } = 50;

    public int RulesTextTopK { get; set; } = 50;

    public double HighCombined { get; set; } = 0.85;

    public double MediumCombined { get; set; } = 0.60;

    public double HighZoneAgreementMinScore { get; set; } = 0.70;

    public int HighZoneAgreementMinCount { get; set; } = 2;
}
