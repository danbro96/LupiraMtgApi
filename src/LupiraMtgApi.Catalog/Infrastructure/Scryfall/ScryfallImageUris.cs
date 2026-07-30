using System.Text.Json.Serialization;

namespace LupiraMtgApi.Catalog.Infrastructure.Scryfall;

public sealed class ScryfallImageUris
{
    [JsonPropertyName("normal")]
    public string? Normal { get; set; }

    [JsonPropertyName("art_crop")]
    public string? ArtCrop { get; set; }

    [JsonPropertyName("large")]
    public string? Large { get; set; }
}
