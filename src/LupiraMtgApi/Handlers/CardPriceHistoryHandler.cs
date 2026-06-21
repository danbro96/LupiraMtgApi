using LupiraMtgApi.Pricing.Application;
using Microsoft.AspNetCore.Http.HttpResults;

namespace LupiraMtgApi.Handlers;

/// <summary>
/// Thin transport adapter over <see cref="PriceHistoryService"/>: calls the Pricing Application service
/// and maps the plain result to <c>TypedResults</c> (null ⇒ 404 when the printing has no recorded points).
/// </summary>
public sealed class CardPriceHistoryHandler
{
    private readonly PriceHistoryService _service;

    public CardPriceHistoryHandler(PriceHistoryService service) => _service = service;

    public async Task<Results<Ok<CardPriceHistoryResponse>, NotFound>> GetHistoryAsync(
        string printingId,
        DateOnly? from,
        DateOnly? to,
        CancellationToken ct)
    {
        var history = await _service.GetAsync(printingId, from, to, ct);
        return history is null ? TypedResults.NotFound() : TypedResults.Ok(history);
    }
}
