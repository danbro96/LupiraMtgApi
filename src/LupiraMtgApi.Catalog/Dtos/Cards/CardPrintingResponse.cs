using LupiraMtgApi.Pricing.Dtos;

namespace LupiraMtgApi.Catalog.Dtos.Cards;

public sealed class CardPrintingResponse
{
    public required string Id { get; set; }

    public required string OracleId { get; set; }

    public required string Name { get; set; }

    public required string SetCode { get; set; }

    public required string SetName { get; set; }

    public required string CollectorNumber { get; set; }

    public required string[] ColorIdentity { get; set; }

    public required string Rarity { get; set; }

    public string? ManaCost { get; set; }

    public float? Cmc { get; set; }

    public CardImageUrls? Images { get; set; }

    public CardPriceResponse? Prices { get; set; }

    // Multi-faced card data — null for normal single-faced cards. Each face has its own
    // name, type line, oracle text, P/T, and (when applicable) a presigned image. Front
    // face is also mirrored to the top-level `name`/`images` fields for back-compat.
    public IReadOnlyList<CardFaceResponse>? Faces { get; set; }
}
