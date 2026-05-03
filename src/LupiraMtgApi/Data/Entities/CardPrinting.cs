namespace LupiraMtgApi.Data.Entities;

public sealed class CardPrinting
{
    public required string Id { get; set; }

    public required string OracleId { get; set; }

    public required string Name { get; set; }

    public required string SetCode { get; set; }

    public required string CollectorNumber { get; set; }

    public required string[] ColorIdentity { get; set; }

    public required string Rarity { get; set; }

    public string? ImageObjectKey { get; set; }

    public string? ImageArtCropKey { get; set; }

    public long? ArtPHash { get; set; }

    public Dictionary<string, decimal>? Prices { get; set; }

    public DateTimeOffset SyncedAt { get; set; }
}
