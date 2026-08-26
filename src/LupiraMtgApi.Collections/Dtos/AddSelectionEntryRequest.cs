namespace LupiraMtgApi.Collections.Dtos;

public sealed class AddSelectionEntryRequest
{
    public required string PrintingId { get; set; }

    public bool IsFoil { get; set; }

    public string Language { get; set; } = "en";

    public string Condition { get; set; } = "NM";

    public double Confidence { get; set; } = 1.0;

    public bool AllowDuplicate { get; set; }
}
