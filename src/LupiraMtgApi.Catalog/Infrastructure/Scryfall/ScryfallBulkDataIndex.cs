using System.Text.Json.Serialization;

namespace LupiraMtgApi.Catalog.Infrastructure.Scryfall;

public sealed class ScryfallBulkDataIndex
{
    [JsonPropertyName("data")]
    public List<ScryfallBulkDataEntry> Data { get; set; } = new();
}
