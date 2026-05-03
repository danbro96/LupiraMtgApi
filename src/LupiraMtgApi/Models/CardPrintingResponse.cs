namespace LupiraMtgApi.Models;

public sealed class CardPrintingResponse
{
    public required string Id { get; set; }

    public required string OracleId { get; set; }

    public required string Name { get; set; }

    public required string SetCode { get; set; }

    public required string SetName { get; set; }

    public required string CollectorNumber { get; set; }

    public required string[] ColorIdentity { get; set; }

    public required string Rarity { get; set; }

    public CardImageUrls? Images { get; set; }

    public Dictionary<string, decimal>? Prices { get; set; }
}

public sealed class CardImageUrls
{
    public string? Normal { get; set; }

    public string? ArtCrop { get; set; }
}
