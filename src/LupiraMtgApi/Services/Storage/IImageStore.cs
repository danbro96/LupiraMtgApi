namespace LupiraMtgApi.Services.Storage;

public interface IImageStore
{
    Task EnsureBucketAsync(CancellationToken ct);

    Task<string> PutAsync(string objectKey, Stream content, string contentType, CancellationToken ct);

    Task<bool> ExistsAsync(string objectKey, CancellationToken ct);

    Task<string> CreatePresignedGetUrlAsync(string objectKey, TimeSpan expiry, CancellationToken ct);

    Task<Stream> GetAsync(string objectKey, CancellationToken ct);
}
