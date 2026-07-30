using System.Text.Json.Serialization;

namespace LupiraMtgApi.Catalog.Infrastructure.Scryfall;

public sealed class ScryfallSetDto
{
    [JsonPropertyName("code")]
    public string Code { get; set; } = string.Empty;

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("set_type")]
    public string SetType { get; set; } = string.Empty;

    [JsonPropertyName("released_at")]
    public string? ReleasedAt { get; set; }

    [JsonPropertyName("card_count")]
    public int CardCount { get; set; }

    [JsonPropertyName("icon_svg_uri")]
    public string? IconSvgUri { get; set; }
}
