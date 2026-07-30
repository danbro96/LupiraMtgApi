namespace LupiraMtgApi.Recognition.Domain;

public sealed class CardZoneScoringResult
{
    public required IReadOnlyDictionary<string, PrintingZoneScores> ByPrinting { get; set; }

    public required ZoneWeights Weights { get; set; }
}
