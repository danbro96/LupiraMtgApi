namespace LupiraMtgApi.Catalog.Dtos.Cards;

/// <summary>
/// One face of a multi-faced card. Returned alongside the parent on <see cref="CardResponse.Faces"/>
/// (oracle level — language-neutral fields) and <see cref="CardPrintingResponse.Faces"/> (printing
/// level — same fields plus presigned per-face image URLs). Single-faced cards leave `faces` null
/// and the client uses the parent's top-level fields.
/// </summary>
public sealed class CardFaceResponse
{
    public required int FaceIndex { get; set; }

    public required string Name { get; set; }

    public string? ManaCost { get; set; }

    public string? TypeLine { get; set; }

    public string? OracleText { get; set; }

    public string? Power { get; set; }

    public string? Toughness { get; set; }

    public CardImageUrls? Images { get; set; }
}
