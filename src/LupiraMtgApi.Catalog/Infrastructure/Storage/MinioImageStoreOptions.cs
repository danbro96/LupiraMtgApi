namespace LupiraMtgApi.Catalog.Infrastructure.Storage;

public sealed class MinioImageStoreOptions
{
    public string Endpoint { get; set; } = string.Empty;

    public string PublicEndpoint { get; set; } = string.Empty;

    public string AccessKey { get; set; } = string.Empty;

    public string SecretKey { get; set; } = string.Empty;

    public string Bucket { get; set; } = "lupira-mtg-cards";

    public bool UseSsl { get; set; }
}
