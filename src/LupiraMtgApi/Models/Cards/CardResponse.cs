namespace LupiraMtgApi.Models.Cards;

public sealed class CardResponse
{
    public required string OracleId { get; set; }

    public required string Name { get; set; }

    public required string TypeLine { get; set; }

    public string? OracleText { get; set; }

    public required string[] ColorIdentity { get; set; }

    public string? ManaCost { get; set; }

    public float? Cmc { get; set; }

    public string? Power { get; set; }

    public string? Toughness { get; set; }

    public required string Layout { get; set; }

    public CardImageUrls? Thumbnail { get; set; }

    public int PrintingCount { get; set; }

    // Multi-faced card data at oracle level — null for normal single-faced cards. The
    // representative printing's faces drive this; image URLs come from that printing.
    public IReadOnlyList<CardFaceResponse>? Faces { get; set; }
}
