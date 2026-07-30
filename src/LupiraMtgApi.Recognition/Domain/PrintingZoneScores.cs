namespace LupiraMtgApi.Recognition.Domain;

public sealed class PrintingZoneScores
{
    public required string PrintingId { get; set; }

    public double NameScore { get; set; }

    public double TypeLineScore { get; set; }

    public double RulesTextScore { get; set; }

    public double PowerToughnessScore { get; set; }

    public double BottomMetadataScore { get; set; }

    public double AggregateScore { get; set; }

    public int ContributingZoneCount(double minScore)
    {
        var count = 0;
        if (this.NameScore >= minScore)
        {
            count++;
        }

        if (this.TypeLineScore >= minScore)
        {
            count++;
        }

        if (this.RulesTextScore >= minScore)
        {
            count++;
        }

        if (this.PowerToughnessScore >= minScore)
        {
            count++;
        }

        if (this.BottomMetadataScore >= minScore)
        {
            count++;
        }

        return count;
    }
}
