using LupiraMtgApi.Domain.Collection;
using LupiraMtgApi.Models.Collections;
using Marten;
using Microsoft.AspNetCore.Http.HttpResults;

namespace LupiraMtgApi.Handlers;

public sealed class MyCardsHandler
{
    private const int DefaultLimit = 50;
    private const int MaxLimit = 200;

    private readonly IDocumentSession _session;
    private readonly CardInstanceHydrator _hydrator;

    public MyCardsHandler(IDocumentSession session, CardInstanceHydrator hydrator)
    {
        _session = session;
        _hydrator = hydrator;
    }

    public async Task<Results<Ok<CardInstanceListResponse>, UnauthorizedHttpResult>> ListAsync(
        HttpContext httpContext,
        int? take,
        int? skip,
        CancellationToken ct)
    {
        if (!httpContext.TryGetOwnerId(out var ownerId))
        {
            return TypedResults.Unauthorized();
        }

        var clampedTake = Math.Clamp(take ?? DefaultLimit, 1, MaxLimit);
        var clampedSkip = Math.Max(skip ?? 0, 0);

        var collections = await Marten.QueryableExtensions.ToListAsync(
            _session.Query<CollectionDocument>()
                .Where(c => c.OwnerId == ownerId && !c.IsRemoved),
            ct);

        var allCards = collections.SelectMany(c => c.Cards).ToList();
        var total = allCards.Count;

        if (total == 0)
        {
            return TypedResults.Ok(new CardInstanceListResponse
            {
                Cards = Array.Empty<CardInstanceResponse>(),
                Total = 0,
            });
        }

        // Sort by printing name across all owned cards. Marten only knows the
        // printing IDs, so look up names from EF before slicing — this keeps the
        // expensive presign work in the hydrator off the cards we won't return.
        var nameByPrintingId = await _hydrator.LoadPrintingNamesAsync(
            allCards.Select(c => c.PrintingId),
            ct);

        var sorted = allCards
            .OrderBy(c => nameByPrintingId.GetValueOrDefault(c.PrintingId, string.Empty), StringComparer.OrdinalIgnoreCase)
            .ThenBy(c => c.InstanceId)
            .Skip(clampedSkip)
            .Take(clampedTake)
            .ToList();

        var collectionByInstance = collections
            .SelectMany(c => c.Cards.Select(card => (Instance: card, Collection: c)))
            .ToDictionary(x => x.Instance.InstanceId, x => x.Collection);

        var hydrated = await _hydrator.HydrateAsync(sorted, collectionId: null, collectionName: null, ct);
        foreach (var card in hydrated)
        {
            if (collectionByInstance.TryGetValue(card.InstanceId, out var owner))
            {
                card.CollectionId = owner.Id;
                card.CollectionName = owner.Name;
            }
        }

        return TypedResults.Ok(new CardInstanceListResponse { Cards = hydrated, Total = total });
    }
}
