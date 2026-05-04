using System.Text.Json.Serialization;

namespace LupiraMtgApi.Services.Scryfall;

public sealed class ScryfallSetsList
{
    [JsonPropertyName("data")]
    public List<ScryfallSetDto> Data { get; set; } = new();
}