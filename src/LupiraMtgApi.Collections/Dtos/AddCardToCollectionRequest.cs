namespace LupiraMtgApi.Collections.Dtos;

public sealed class AddCardToCollectionRequest
{
    public required string PrintingId { get; set; }

    public bool IsFoil { get; set; }

    public string Language { get; set; } = "en";

    public string Condition { get; set; } = "NM";
}
