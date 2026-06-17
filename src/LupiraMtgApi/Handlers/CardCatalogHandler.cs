using LupiraMtgApi.Catalog.Application;
using LupiraMtgApi.Models.Cards;
using Microsoft.AspNetCore.Http.HttpResults;

namespace LupiraMtgApi.Handlers;

/// <summary>
/// Thin transport adapter over <see cref="CardCatalogService"/>: binds the query, calls the Catalog
/// Application service, and maps the plain result to <c>TypedResults</c> (null ⇒ 404).
/// </summary>
public sealed class CardCatalogHandler
{
    private readonly CardCatalogService _service;

    public CardCatalogHandler(CardCatalogService service) => _service = service;

    public async Task<Ok<CardListResponse>> ListCardsAsync(CardListQuery query, CancellationToken ct) =>
        TypedResults.Ok(await _service.ListCardsAsync(ToRequest(query), ct));

    public async Task<Results<Ok<CardResponse>, NotFound>> GetCardAsync(string oracleId, CancellationToken ct)
    {
        var card = await _service.GetCardAsync(oracleId, ct);
        return card is null ? TypedResults.NotFound() : TypedResults.Ok(card);
    }

    public async Task<Results<Ok<CardPrintingListResponse>, NotFound>> ListPrintingsAsync(string oracleId, CancellationToken ct)
    {
        var list = await _service.ListPrintingsAsync(oracleId, ct);
        return list is null ? TypedResults.NotFound() : TypedResults.Ok(list);
    }

    public async Task<Results<Ok<CardPrintingResponse>, NotFound>> GetPrintingAsync(string oracleId, string printingId, CancellationToken ct)
    {
        var printing = await _service.GetPrintingAsync(oracleId, printingId, ct);
        return printing is null ? TypedResults.NotFound() : TypedResults.Ok(printing);
    }

    private static CardListRequest ToRequest(CardListQuery q) => new()
    {
        Q = q.Q,
        Set = q.Set,
        Color = q.Color,
        Colors = q.Colors,
        Rarity = q.Rarity,
        Type = q.Type,
        Cmc = q.Cmc,
        CmcMin = q.CmcMin,
        CmcMax = q.CmcMax,
        Power = q.Power,
        Toughness = q.Toughness,
        Sort = q.Sort,
        Order = q.Order,
        Take = q.Take,
        Skip = q.Skip,
    };
}
