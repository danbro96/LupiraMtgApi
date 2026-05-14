using Microsoft.AspNetCore.Mvc;

namespace LupiraMtgApi.Models.Cards;

/// <summary>
/// Bound from query string via <c>[AsParameters]</c> on <c>GET /cards</c>. Each property
/// names its URL parameter via <see cref="FromQueryAttribute"/> so the wire-level convention
/// stays camelCase even though C# property names are PascalCase.
/// </summary>
public sealed class CardListQuery
{
    [FromQuery(Name = "q")] public string? Q { get; set; }

    [FromQuery(Name = "set")] public string? Set { get; set; }

    [FromQuery(Name = "color")] public string? Color { get; set; }

    [FromQuery(Name = "colors")] public string? Colors { get; set; }

    [FromQuery(Name = "rarity")] public string? Rarity { get; set; }

    [FromQuery(Name = "type")] public string? Type { get; set; }

    [FromQuery(Name = "cmc")] public float? Cmc { get; set; }

    [FromQuery(Name = "cmcMin")] public float? CmcMin { get; set; }

    [FromQuery(Name = "cmcMax")] public float? CmcMax { get; set; }

    [FromQuery(Name = "power")] public string? Power { get; set; }

    [FromQuery(Name = "toughness")] public string? Toughness { get; set; }

    [FromQuery(Name = "sort")] public string? Sort { get; set; }

    [FromQuery(Name = "order")] public string? Order { get; set; }

    [FromQuery(Name = "take")] public int? Take { get; set; }

    [FromQuery(Name = "skip")] public int? Skip { get; set; }
}
