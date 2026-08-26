using LupiraMtgApi.Catalog.Dtos.Cards;

namespace LupiraMtgApi.Recognition.Dtos;

public sealed class CardCandidateDto
{
    public required CardPrintingDto Printing { get; set; }

    public required double CombinedScore { get; set; }

    public required double OcrAggregateScore { get; set; }

    public required double NameScore { get; set; }

    public required double TypeLineScore { get; set; }

    public required double RulesTextScore { get; set; }

    public required double PowerToughnessScore { get; set; }

    public required double BottomMetadataScore { get; set; }

    public required double HammingScore { get; set; }

    public required double SetTypeWeight { get; set; }

    public required int? HammingDistance { get; set; }

    public required bool MatchedByPHash { get; set; }

    public required bool MatchedByName { get; set; }
}
