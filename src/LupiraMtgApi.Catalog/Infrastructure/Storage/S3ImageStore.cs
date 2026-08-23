using Amazon.Runtime;
using Amazon.S3;
using Amazon.S3.Model;

namespace LupiraMtgApi.Catalog.Infrastructure.Storage;

/// <summary>AWSSDK.S3 implementation over Garage. Two clients on the same credentials: operations run
/// against the in-network endpoint; presigning runs against the public endpoint (SigV4 signs the host,
/// and signing is offline — no request ever leaves the presign client).</summary>
public sealed class S3ImageStore : IImageStore, IDisposable
{
    private readonly AmazonS3Client _client;
    private readonly AmazonS3Client _publicClient;
    private readonly Protocol _presignProtocol;
    private readonly S3ImageStoreOptions _options;

    public S3ImageStore(IOptions<S3ImageStoreOptions> options)
    {
        _options = options.Value;
        var credentials = new BasicAWSCredentials(_options.AccessKey, _options.SecretKey);
        _client = new AmazonS3Client(credentials, Config(_options.ServiceUrl, _options.Region));
        _publicClient = new AmazonS3Client(credentials, Config(_options.PublicUrl, _options.Region));
        _presignProtocol = _options.PublicUrl.StartsWith("https", StringComparison.OrdinalIgnoreCase)
            ? Protocol.HTTPS
            : Protocol.HTTP;
    }

    private static AmazonS3Config Config(string serviceUrl, string region) => new()
    {
        ServiceURL = serviceUrl,
        ForcePathStyle = true,
        AuthenticationRegion = region,
        // SDK v4 defaults to CRC checksum trailers that third-party S3 backends reject.
        RequestChecksumCalculation = RequestChecksumCalculation.WHEN_REQUIRED,
        ResponseChecksumValidation = ResponseChecksumValidation.WHEN_REQUIRED,
    };

    public async Task EnsureBucketAsync(CancellationToken ct)
    {
        // Least-privilege keys can't create buckets — existence check only, with a pointed error.
        try
        {
            await _client.ListObjectsV2Async(new ListObjectsV2Request { BucketName = _options.Bucket, MaxKeys = 1 }, ct);
        }
        catch (AmazonS3Exception ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            throw new InvalidOperationException(
                $"Bucket '{_options.Bucket}' not found — create it via the garage CLI (see deploy docs).", ex);
        }
    }

    public async Task<string> PutAsync(string objectKey, Stream content, string contentType, CancellationToken ct)
    {
        await _client.PutObjectAsync(new PutObjectRequest
        {
            BucketName = _options.Bucket,
            Key = objectKey,
            InputStream = content,
            AutoCloseStream = false,
            ContentType = contentType,
            // Content-length'd single-shot body — no aws-chunked encoding for third-party compatibility.
            UseChunkEncoding = false,
        }, ct);
        return objectKey;
    }

    public async Task<bool> ExistsAsync(string objectKey, CancellationToken ct)
    {
        try
        {
            await _client.GetObjectMetadataAsync(new GetObjectMetadataRequest { BucketName = _options.Bucket, Key = objectKey }, ct);
            return true;
        }
        catch (AmazonS3Exception ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return false;
        }
    }

    public async Task<string> CreatePresignedGetUrlAsync(string objectKey, TimeSpan expiry, CancellationToken ct)
    {
        // SigV4 hard limit: 7 days.
        var capped = TimeSpan.FromSeconds(Math.Min(expiry.TotalSeconds, 60 * 60 * 24 * 7));
        return await _publicClient.GetPreSignedURLAsync(new GetPreSignedUrlRequest
        {
            BucketName = _options.Bucket,
            Key = objectKey,
            Verb = HttpVerb.GET,
            Protocol = _presignProtocol,
            Expires = DateTime.UtcNow + capped,
        });
    }

    public async Task<Stream> GetAsync(string objectKey, CancellationToken ct)
    {
        using var response = await _client.GetObjectAsync(new GetObjectRequest { BucketName = _options.Bucket, Key = objectKey }, ct);
        var ms = new MemoryStream();
        await response.ResponseStream.CopyToAsync(ms, ct);
        ms.Position = 0;
        return ms;
    }

    public void Dispose()
    {
        _client.Dispose();
        _publicClient.Dispose();
    }
}
