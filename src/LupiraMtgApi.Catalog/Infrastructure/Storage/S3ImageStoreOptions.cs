namespace LupiraMtgApi.Catalog.Infrastructure.Storage;

/// <summary>Bound from the <c>S3</c> section. Vendor-neutral S3 wire protocol — the deployed backend is
/// Garage, reached in-network for operations and presigned against the public hostname.</summary>
public sealed class S3ImageStoreOptions
{
    /// <summary>In-network endpoint for PUT/GET/HEAD (e.g. http://garage:3900).</summary>
    public string ServiceUrl { get; set; } = string.Empty;

    /// <summary>Public endpoint presigned URLs are minted against (e.g. https://s3.lupira.com) —
    /// SigV4 signs the host, so clients must see the same hostname the signature carries.</summary>
    public string PublicUrl { get; set; } = string.Empty;

    /// <summary>Anonymous health endpoint for the /depz probe (Garage admin port, e.g. http://garage:3903).</summary>
    public string HealthUrl { get; set; } = string.Empty;

    public string AccessKey { get; set; } = string.Empty;

    public string SecretKey { get; set; } = string.Empty;

    public string Bucket { get; set; } = "lupira-mtg-cards";

    /// <summary>Must match the store's configured region (Garage: <c>s3_region</c>) or signatures fail.</summary>
    public string Region { get; set; } = "garage";
}
