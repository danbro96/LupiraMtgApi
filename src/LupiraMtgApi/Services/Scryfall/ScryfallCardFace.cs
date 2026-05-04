using System.Text.Json.Serialization;

namespace LupiraMtgApi.Services.Scryfall;

public sealed class ScryfallCardFace
{
    [JsonPropertyName("name")]
    public string? Name { get; set; }

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
}