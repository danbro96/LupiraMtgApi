namespace LupiraMtgApi.Collections.Dtos;

public sealed class BulkAddCardItem
{
    public required string PrintingId { get; set; }

    public bool IsFoil { get; set; }

    public string Language { get; set; } = "en";

    public string Condition { get; set; } = "NM";

    // Add this many identical instances. Default 1; clamped to [1, 50] per item.
    public int? Count { get; set; }
}
