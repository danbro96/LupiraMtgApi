using System.Text.Json.Serialization;

namespace LupiraMtgApi.Catalog.Infrastructure.Scryfall;

public sealed class ScryfallCardFace
{
    [JsonPropertyName("name")]
    public string? Name { get; set; }

    [JsonPropertyName("mana_cost")]
    public string? ManaCost { get; set; }

    [JsonPropertyName("type_line")]
    public string? TypeLine { get; set; }

    [JsonPropertyName("printed_type_line")]
    public string? PrintedTypeLine { get; set; }

    [JsonPropertyName("oracle_text")]
    public string? OracleText { get; set; }

    [JsonPropertyName("printed_text")]
    public string? PrintedText { get; set; }

    [JsonPropertyName("power")]
    public string? Power { get; set; }

    [JsonPropertyName("toughness")]
    public string? Toughness { get; set; }

    // Only populated for layouts where each face has its own image (transform,
    // modal_dfc, double_faced_token, reversible_card). Layouts that share the
    // parent image across faces (split, flip, adventure, meld) leave this null.
    [JsonPropertyName("image_uris")]
    public ScryfallImageUris? ImageUris { get; set; }
}
