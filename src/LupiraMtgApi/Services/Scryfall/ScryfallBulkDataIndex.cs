using System.Text.Json.Serialization;

namespace LupiraMtgApi.Services.Scryfall;

public sealed class ScryfallBulkDataIndex
{
    [JsonPropertyName("data")]
    public List<ScryfallBulkDataEntry> Data { get; set; } = new();
}