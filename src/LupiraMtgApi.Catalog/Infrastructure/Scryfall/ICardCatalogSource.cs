namespace LupiraMtgApi.Catalog.Infrastructure.Scryfall;

public interface ICardCatalogSource
{
    Task<ScryfallBulkDataEntry> GetDefaultCardsBulkEntryAsync(CancellationToken ct);

    IAsyncEnumerable<ScryfallCardDto> StreamCardsAsync(string downloadUri, CancellationToken ct);

    Task<IReadOnlyList<ScryfallSetDto>> GetSetsAsync(CancellationToken ct);

    Task<Stream> DownloadImageAsync(string url, CancellationToken ct);
}
