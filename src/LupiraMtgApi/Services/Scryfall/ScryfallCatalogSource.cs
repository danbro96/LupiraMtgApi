using System.Runtime.CompilerServices;
using System.Text.Json;

namespace LupiraMtgApi.Services.Scryfall;

public sealed class ScryfallCatalogSource : ICardCatalogSource
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    private readonly HttpClient _httpClient;
    private readonly ILogger<ScryfallCatalogSource> _logger;

    public ScryfallCatalogSource(HttpClient httpClient, ILogger<ScryfallCatalogSource> logger)
    {
        _httpClient = httpClient;
        _logger = logger;
    }

    public async Task<ScryfallBulkDataEntry> GetDefaultCardsBulkEntryAsync(CancellationToken ct)
    {
        var index = await _httpClient.GetFromJsonAsync<ScryfallBulkDataIndex>("bulk-data", SerializerOptions, ct)
            ?? throw new InvalidOperationException("Scryfall bulk-data index returned null.");

        var entry = index.Data.FirstOrDefault(e => e.Type == "default_cards")
            ?? throw new InvalidOperationException("Scryfall bulk-data has no entry of type 'default_cards'.");

        _logger.LogInformation(
            "Scryfall default_cards bulk: {Size} bytes, updated {UpdatedAt}",
            entry.Size,
            entry.UpdatedAt);

        return entry;
    }

    public async IAsyncEnumerable<ScryfallCardDto> StreamCardsAsync(
        string downloadUri,
        [EnumeratorCancellation] CancellationToken ct)
    {
        using var response = await _httpClient.GetAsync(
            downloadUri,
            HttpCompletionOption.ResponseHeadersRead,
            ct);
        response.EnsureSuccessStatusCode();

        await using var stream = await response.Content.ReadAsStreamAsync(ct);

        await foreach (var card in JsonSerializer.DeserializeAsyncEnumerable<ScryfallCardDto>(
            stream,
            SerializerOptions,
            ct))
        {
            if (card is not null)
            {
                yield return card;
            }
        }
    }

    public async Task<IReadOnlyList<ScryfallSetDto>> GetSetsAsync(CancellationToken ct)
    {
        var list = await _httpClient.GetFromJsonAsync<ScryfallSetsList>("sets", SerializerOptions, ct)
            ?? throw new InvalidOperationException("Scryfall sets endpoint returned null.");

        return list.Data;
    }

    public async Task<Stream> DownloadImageAsync(string url, CancellationToken ct)
    {
        var response = await _httpClient.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadAsStreamAsync(ct);
    }
}
