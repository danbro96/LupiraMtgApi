namespace LupiraMtgApi.Services;

public interface IOcrService
{
    Task<string> ReadTextAsync(byte[] imageBytes, string mediaType, CancellationToken ct);

    Task<OcrRegions> ReadRegionsAsync(byte[] imageBytes, string mediaType, CancellationToken ct);
}
