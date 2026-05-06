namespace LupiraMtgApi.Data.Entities;

public sealed class CardPrinting
{
    public required string Id { get; set; }

    public required string OracleId { get; set; }

    public required string Name { get; set; }

    public required string SetCode { get; set; }

    public required string CollectorNumber { get; set; }

    public required string[] ColorIdentity { get; set; }

    public required string Rarity { get; set; }

    public string? ImageObjectKey { get; set; }

    public string? ImageArtCropKey { get; set; }

    public long? ArtPHash { get; set; }

    // Perceptual hash of the full card image (Scryfall image_uris.normal). Captures
    // frame + name + art + text together — strictly more information than ArtPHash but
    // more sensitive to lighting/foil. Loaded into a separate BK-tree alongside
    // ArtPHash; the scan path searches both and takes whichever rotation/index gives
    // the lower hamming distance per candidate printing.
    public long? FullCardPHash { get; set; }

    public Dictionary<string, decimal>? Prices { get; set; }

    public DateTimeOffset SyncedAt { get; set; }

    public string? Supertype { get; set; }

    public string Type { get; set; } = string.Empty;

    public string? Subtype { get; set; }

    // Postgres GENERATED ALWAYS AS … STORED column (see DbContext). Always reflects the
    // recomposed full type line; trigram index lives here so matching uses the whole.
    public string? TypeLineFull { get; set; }

    // Printed rules text on this specific printing (Scryfall printed_text, falls back to
    // oracle_text when absent). What OCR sees on the card; used for matching.
    public string? RulesText { get; set; }

    // Canonical English oracle text. Kept for display; not used for matching.
    public string? OracleText { get; set; }

    public string? Power { get; set; }

    public string? Toughness { get; set; }

    public string Lang { get; set; } = "en";

    public string Layout { get; set; } = string.Empty;

    public bool IsFoil { get; set; }
}
