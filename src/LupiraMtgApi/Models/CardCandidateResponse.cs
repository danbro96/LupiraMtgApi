namespace LupiraMtgApi.Models;

public sealed class CardCandidateResponse
{
    public required CardPrintingResponse Printing { get; set; }

    public required double CombinedScore { get; set; }

    public required double OcrAggregateScore { get; set; }

    public required double NameScore { get; set; }

    public required double TypeLineScore { get; set; }

    public required double RulesTextScore { get; set; }

    public required double PowerToughnessScore { get; set; }

    public required double BottomMetadataScore { get; set; }

    public required double HammingScore { get; set; }

    public int? HammingDistance { get; set; }

    public bool MatchedByPHash { get; set; }

    public bool MatchedByName { get; set; }
}
