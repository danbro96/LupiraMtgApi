namespace LupiraMtgApi.Models;

public sealed class CardCandidateResponse
{
    public required CardPrintingResponse Printing { get; set; }

    public required double CombinedScore { get; set; }

    public required double NameScore { get; set; }

    public required double HammingScore { get; set; }

    public int? HammingDistance { get; set; }

    public bool MatchedByPHash { get; set; }

    public bool MatchedByName { get; set; }
}
