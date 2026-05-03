using Microsoft.Extensions.Options;
using Minio;
using Minio.DataModel.Args;
using Minio.Exceptions;

namespace LupiraMtgApi.Services;

public sealed class MinioImageStore : IImageStore
{
    private readonly IMinioClient client;
    private readonly IMinioClient publicClient;
    private readonly MinioImageStoreOptions options;
    private readonly ILogger<MinioImageStore> logger;

    public MinioImageStore(IOptions<MinioImageStoreOptions> options, ILogger<MinioImageStore> logger)
    {
        this.options = options.Value;
        this.logger = logger;

        this.client = new MinioClient()
            .WithEndpoint(this.options.Endpoint)
            .WithCredentials(this.options.AccessKey, this.options.SecretKey)
            .WithSSL(this.options.UseSsl)
            .Build();

        var publicHost = ExtractHost(this.options.PublicEndpoint);
        var publicSsl = this.options.PublicEndpoint.StartsWith("https://", StringComparison.OrdinalIgnoreCase);
        this.publicClient = new MinioClient()
            .WithEndpoint(publicHost)
            .WithCredentials(this.options.AccessKey, this.options.SecretKey)
            .WithSSL(publicSsl)
            .Build();
    }

    public async Task EnsureBucketAsync(CancellationToken ct)
    {
        var exists = await this.client.BucketExistsAsync(
            new BucketExistsArgs().WithBucket(this.options.Bucket),
            ct);

        if (!exists)
        {
            await this.client.MakeBucketAsync(
                new MakeBucketArgs().WithBucket(this.options.Bucket),
                ct);
            this.logger.LogInformation("Created MinIO bucket {Bucket}", this.options.Bucket);
        }
    }

    public async Task<string> PutAsync(string objectKey, Stream content, string contentType, CancellationToken ct)
    {
        var args = new PutObjectArgs()
            .WithBucket(this.options.Bucket)
            .WithObject(objectKey)
            .WithStreamData(content)
            .WithObjectSize(content.CanSeek ? content.Length : -1)
            .WithContentType(contentType);

        await this.client.PutObjectAsync(args, ct);
        return objectKey;
    }

    public async Task<bool> ExistsAsync(string objectKey, CancellationToken ct)
    {
        try
        {
            await this.client.StatObjectAsync(
                new StatObjectArgs().WithBucket(this.options.Bucket).WithObject(objectKey),
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
        var seconds = (int)Math.Min(expiry.TotalSeconds, 60 * 60 * 24 * 7);
        var args = new PresignedGetObjectArgs()
            .WithBucket(this.options.Bucket)
            .WithObject(objectKey)
            .WithExpiry(seconds);

        return await this.publicClient.PresignedGetObjectAsync(args);
    }

    public async Task<Stream> GetAsync(string objectKey, CancellationToken ct)
    {
        var ms = new MemoryStream();
        var args = new GetObjectArgs()
            .WithBucket(this.options.Bucket)
            .WithObject(objectKey)
            .WithCallbackStream(s => s.CopyTo(ms));
        await this.client.GetObjectAsync(args, ct);
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
