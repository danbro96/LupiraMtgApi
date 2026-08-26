namespace LupiraMtgApi.Recognition.Domain;

public sealed class ScanLogCandidate
{
    public required string PrintingId { get; set; }

    public required string SetCode { get; set; }

    public string? SetType { get; set; }

    public double SetTypeWeight { get; set; }

    public double CombinedScore { get; set; }

    public double OcrAggregateScore { get; set; }

    public double NameScore { get; set; }

    public double TypeLineScore { get; set; }

    public double RulesTextScore { get; set; }

    public double PowerToughnessScore { get; set; }

    public double BottomMetadataScore { get; set; }

    public double HammingScore { get; set; }

    public int? HammingDistance { get; set; }

    public bool MatchedByPHash { get; set; }

    public bool MatchedByName { get; set; }
}
