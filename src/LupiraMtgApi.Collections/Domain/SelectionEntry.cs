namespace LupiraMtgApi.Collections.Domain;

public sealed class SelectionEntry
{
    public required Guid InstanceId { get; set; }

    public required string PrintingId { get; set; }

    public required bool IsFoil { get; set; }

    public required string Language { get; set; }

    public required string Condition { get; set; }

    public required double Confidence { get; set; }
}
