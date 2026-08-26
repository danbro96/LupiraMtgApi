namespace LupiraMtgApi.Catalog.Dtos.Cards;

/// <summary>
/// One face of a multi-faced card. Returned alongside the parent on <see cref="CardDto.Faces"/>
/// (oracle level — language-neutral fields) and <see cref="CardPrintingDto.Faces"/> (printing
/// level — same fields plus presigned per-face image URLs). Single-faced cards leave `faces` null
/// and the client uses the parent's top-level fields.
/// </summary>
public sealed class CardFaceDto
{
    public required int FaceIndex { get; set; }

    public required string Name { get; set; }

    public required string? ManaCost { get; set; }

    public required string? TypeLine { get; set; }

    public required string? OracleText { get; set; }

    public required string? Power { get; set; }

    public required string? Toughness { get; set; }

    public required CardImageUrls? Images { get; set; }
}
