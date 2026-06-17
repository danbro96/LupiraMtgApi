namespace LupiraMtgApi.Catalog.Dtos.Cards;

/// <summary>
/// Transport-neutral card-search parameters consumed by <c>CardCatalogService</c>. The host binds
/// the query string into its own <c>[FromQuery]</c>-annotated type and maps it onto this plain record,
/// keeping ASP.NET model-binding attributes out of the Catalog context.
/// </summary>
public sealed class CardListRequest
{
    public string? Q { get; set; }

    public string? Set { get; set; }

    public string? Color { get; set; }

    public string? Colors { get; set; }

    public string? Rarity { get; set; }

    public string? Type { get; set; }

    public float? Cmc { get; set; }

    public float? CmcMin { get; set; }

    public float? CmcMax { get; set; }

    public string? Power { get; set; }

    public string? Toughness { get; set; }

    public string? Sort { get; set; }

    public string? Order { get; set; }

    public int? Take { get; set; }

    public int? Skip { get; set; }
}
