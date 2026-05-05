using System.Text.Json.Serialization;

namespace LupiraMtgApi.Services.Scryfall;

public sealed class ScryfallCardDto
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("oracle_id")]
    public string? OracleId { get; set; }

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("set")]
    public string SetCode { get; set; } = string.Empty;

    [JsonPropertyName("collector_number")]
    public string CollectorNumber { get; set; } = string.Empty;

    [JsonPropertyName("color_identity")]
    public string[] ColorIdentity { get; set; } = Array.Empty<string>();

    [JsonPropertyName("rarity")]
    public string Rarity { get; set; } = string.Empty;

    [JsonPropertyName("image_uris")]
    public ScryfallImageUris? ImageUris { get; set; }

    [JsonPropertyName("prices")]
    public ScryfallPrices? Prices { get; set; }

    [JsonPropertyName("digital")]
    public bool IsDigital { get; set; }

    [JsonPropertyName("layout")]
    public string Layout { get; set; } = string.Empty;

    [JsonPropertyName("lang")]
    public string? Lang { get; set; }

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

    [JsonPropertyName("card_faces")]
    public ScryfallCardFace[]? CardFaces { get; set; }

    [JsonPropertyName("foil")]
    public bool IsFoil { get; set; }
}