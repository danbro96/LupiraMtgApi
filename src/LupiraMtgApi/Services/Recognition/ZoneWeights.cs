namespace LupiraMtgApi.Services.Recognition;

public sealed class ZoneWeights
{
    public bool NamePresent { get; set; }

    public bool TypeLinePresent { get; set; }

    public bool RulesTextPresent { get; set; }

    public bool PowerToughnessPresent { get; set; }

    public bool BottomMetadataPresent { get; set; }

    public double TotalPresent { get; set; }
}