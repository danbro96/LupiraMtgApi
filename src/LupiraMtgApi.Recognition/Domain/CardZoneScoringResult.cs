namespace LupiraMtgApi.Recognition.Domain;

public sealed class CardZoneScoringResult
{
    public required IReadOnlyDictionary<string, PrintingZoneScores> ByPrinting { get; set; }

    public required ZoneWeights Weights { get; set; }

    public double BestAggregateScore => ByPrinting.Count == 0 ? 0.0 : ByPrinting.Values.Max(s => s.AggregateScore);

    /// <summary>Excludes pHash seeds no OCR zone matched.</summary>
    public int OcrMatchedCount => ByPrinting.Values.Count(s => s.AggregateScore > 0);
}
