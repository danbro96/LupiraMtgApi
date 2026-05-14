namespace LupiraMtgApi.Data.Entities;

/// <summary>
/// One face of a multi-faced printing (transform / modal_dfc / split / flip / adventure / meld).
/// Single-faced cards leave <see cref="CardPrinting.Faces"/> null and use the top-level columns.
/// The front face is also mirrored to the top-level columns for back-compat with the recognizer
/// pipeline, which is front-face-only by design.
/// </summary>
public sealed class CardFace
{
    public required int FaceIndex { get; set; }

    public required string Name { get; set; }

    public string? ManaCost { get; set; }

    public string? TypeLine { get; set; }

    public string? OracleText { get; set; }

    public string? Power { get; set; }

    public string? Toughness { get; set; }

    // Per-face image keys. Only populated for layouts where each face has its own
    // image_uris (transform, modal_dfc, double_faced_token, reversible_card). Layouts
    // that share one image across faces (split, flip, adventure, meld) leave these null —
    // clients should fall back to the parent CardPrinting's image keys.
    public string? ImageObjectKey { get; set; }

    public string? ImageArtCropKey { get; set; }
}
