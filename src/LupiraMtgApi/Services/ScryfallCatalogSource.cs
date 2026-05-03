using System.Net.Http.Json;
using System.Runtime.CompilerServices;
using System.Text.Json;

namespace LupiraMtgApi.Services;

public sealed class ScryfallCatalogSource : ICardCatalogSource
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    private readonly HttpClient httpClient;
    private readonly ILogger<ScryfallCatalogSource> logger;

    public ScryfallCatalogSource(HttpClient httpClient, ILogger<ScryfallCatalogSource> logger)
    {
        this.httpClient = httpClient;
        this.logger = logger;
    }

    public async Task<ScryfallBulkDataEntry> GetDefaultCardsBulkEntryAsync(CancellationToken ct)
    {
        var index = await this.httpClient.GetFromJsonAsync<ScryfallBulkDataIndex>("bulk-data", SerializerOptions, ct)
            ?? throw new InvalidOperationException("Scryfall bulk-data index returned null.");

        var entry = index.Data.FirstOrDefault(e => e.Type == "default_cards")
            ?? throw new InvalidOperationException("Scryfall bulk-data has no entry of type 'default_cards'.");

        this.logger.LogInformation(
            "Scryfall default_cards bulk: {Size} bytes, updated {UpdatedAt}",
            entry.Size,
            entry.UpdatedAt);

        return entry;
    }

    public async IAsyncEnumerable<ScryfallCardDto> StreamCardsAsync(
        string downloadUri,
        [EnumeratorCancellation] CancellationToken ct)
    {
        using var response = await this.httpClient.GetAsync(
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
        var list = await this.httpClient.GetFromJsonAsync<ScryfallSetsList>("sets", SerializerOptions, ct)
            ?? throw new InvalidOperationException("Scryfall sets endpoint returned null.");

        return list.Data;
    }

    public async Task<Stream> DownloadImageAsync(string url, CancellationToken ct)
    {
        var response = await this.httpClient.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadAsStreamAsync(ct);
    }
}
