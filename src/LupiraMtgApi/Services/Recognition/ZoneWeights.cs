namespace LupiraMtgApi.Services.Recognition;

public sealed class ZoneWeights
{
    public bool NamePresent { get; set; }

    public bool TypeLinePresent { get; set; }

    public bool RulesTextPresent { get; set; }

    public bool PowerToughnessPresent { get; set; }

    public bool BottomMetadataPresent { get; set; }

    /// <summary>Effective per-zone weight after confidence smoothing; 0 when the zone is absent.</summary>
    public double NameWeight { get; set; }

    public double TypeLineWeight { get; set; }

    public double RulesTextWeight { get; set; }

    public double PowerToughnessWeight { get; set; }

    public double BottomMetadataWeight { get; set; }

    /// <summary>Sum of the effective per-zone weights; <c>&gt; 0</c> iff at least one zone is present.</summary>
    public double TotalPresent { get; set; }
}
