namespace LupiraMtgApi.Recognition.Domain;

/// <summary>Collector line parsed from a card's bottom-left metadata. Values are normalised to catalogue form.</summary>
public sealed class BottomMetadata
{
    /// <summary>Leading zeros stripped ("0163" → "163"), matching Scryfall's collector numbers.</summary>
    public required string CollectorNumber { get; set; }

    /// <summary>Scryfall rarity name ("uncommon"), or null when no known letter was read.</summary>
    public string? Rarity { get; set; }

    /// <summary>Lower-case set code, or null when unreadable.</summary>
    public string? SetCode { get; set; }

    /// <summary>Scryfall language code ("en", "ja", "zhs"), or null when unreadable.</summary>
    public string? Lang { get; set; }
}
