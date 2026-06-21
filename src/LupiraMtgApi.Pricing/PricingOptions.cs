namespace LupiraMtgApi.Pricing;

public sealed class PricingOptions
{
    /// <summary>How far back <c>GET .../prices</c> history reads; older points are excluded (and may be pruned later).</summary>
    public int HistoryRetentionDays { get; set; } = 365;
}
