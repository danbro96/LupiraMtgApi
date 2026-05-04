using Microsoft.Extensions.Options;
using Minio;
using Minio.DataModel.Args;
using Minio.Exceptions;

namespace LupiraMtgApi.Services.Storage;

public sealed class MinioImageStore : IImageStore
{
    private readonly IMinioClient _client;
    private readonly IMinioClient _publicClient;
    private readonly MinioImageStoreOptions _options;
    private readonly ILogger<MinioImageStore> _logger;

    public MinioImageStore(IOptions<MinioImageStoreOptions> options, ILogger<MinioImageStore> logger)
    {
        _options = options.Value;
        _logger = logger;

        _client = new MinioClient()
            .WithEndpoint(_options.Endpoint)
            .WithCredentials(_options.AccessKey, _options.SecretKey)
            .WithSSL(_options.UseSsl)
            .Build();

        var publicHost = ExtractHost(_options.PublicEndpoint);
        var publicSsl = _options.PublicEndpoint.StartsWith("https://", StringComparison.OrdinalIgnoreCase);
        _publicClient = new MinioClient()
            .WithEndpoint(publicHost)
            .WithCredentials(_options.AccessKey, _options.SecretKey)
            .WithSSL(publicSsl)
            .Build();
    }

    public async Task EnsureBucketAsync(CancellationToken ct)
    {
        var exists = await _client.BucketExistsAsync(
            new BucketExistsArgs().WithBucket(_options.Bucket),
            ct);

        if (!exists)
        {
            await _client.MakeBucketAsync(
                new MakeBucketArgs().WithBucket(_options.Bucket),
                ct);
            _logger.LogInformation("Created MinIO bucket {Bucket}", _options.Bucket);
        }
    }

    public async Task<string> PutAsync(string objectKey, Stream content, string contentType, CancellationToken ct)
    {
        var args = new PutObjectArgs()
            .WithBucket(_options.Bucket)
            .WithObject(objectKey)
            .WithStreamData(content)
            .WithObjectSize(content.CanSeek ? content.Length : -1)
            .WithContentType(contentType);

        await _client.PutObjectAsync(args, ct);
        return objectKey;
    }

    public async Task<bool> ExistsAsync(string objectKey, CancellationToken ct)
    {
        try
        {
            await _client.StatObjectAsync(
                new StatObjectArgs().WithBucket(_options.Bucket).WithObject(objectKey),
                ct);
            return true;
        }
        catch (ObjectNotFoundException)
        {
            return false;
        }
    }

    public async Task<string> CreatePresignedGetUrlAsync(string objectKey, TimeSpan expiry, CancellationToken ct)
    {
        var seconds = (int) Math.Min(expiry.TotalSeconds, 60 * 60 * 24 * 7);
        var args = new PresignedGetObjectArgs()
            .WithBucket(_options.Bucket)
            .WithObject(objectKey)
            .WithExpiry(seconds);

        return await _publicClient.PresignedGetObjectAsync(args);
    }

    public async Task<Stream> GetAsync(string objectKey, CancellationToken ct)
    {
        var ms = new MemoryStream();
        var args = new GetObjectArgs()
            .WithBucket(_options.Bucket)
            .WithObject(objectKey)
            .WithCallbackStream(s => s.CopyTo(ms));
        await _client.GetObjectAsync(args, ct);
        ms.Position = 0;
        return ms;
    }

    private static string ExtractHost(string url)
    {
        if (Uri.TryCreate(url, UriKind.Absolute, out var uri))
        {
            return uri.Port == 80 || uri.Port == 443 || uri.IsDefaultPort
                ? uri.Host
                : $"{uri.Host}:{uri.Port}";
        }

        return url;
    }
}
