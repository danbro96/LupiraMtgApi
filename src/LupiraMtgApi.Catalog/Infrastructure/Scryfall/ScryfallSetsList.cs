using System.Text.Json.Serialization;

namespace LupiraMtgApi.Catalog.Infrastructure.Scryfall;

public sealed class ScryfallSetsList
{
    [JsonPropertyName("data")]
    public List<ScryfallSetDto> Data { get; set; } = new();
}
