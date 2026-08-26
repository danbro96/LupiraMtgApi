namespace LupiraMtgApi.Catalog.Dtos.Cards;

public sealed class CardResponse
{
    public required string OracleId { get; set; }

    public required string Name { get; set; }

    public required string TypeLine { get; set; }

    public required string? OracleText { get; set; }

    public required string[] ColorIdentity { get; set; }

    public required string? ManaCost { get; set; }

    public required float? Cmc { get; set; }

    public required string? Power { get; set; }

    public required string? Toughness { get; set; }

    public required string Layout { get; set; }

    public required CardImageUrls? Thumbnail { get; set; }

    public required int PrintingCount { get; set; }

    // Multi-faced card data at oracle level — null for normal single-faced cards. The
    // representative printing's faces drive this; image URLs come from that printing.
    public required IReadOnlyList<CardFaceResponse>? Faces { get; set; }
}
