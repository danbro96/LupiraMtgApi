using LupiraMtgApi.Collections.Domain;
using LupiraMtgApi.Collections.Dtos;
using LupiraMtgApi.Collections.Mappers;
using Marten;

namespace LupiraMtgApi.Collections.Application;

/// <summary>
/// Reads the caller's entire owned card inventory across all their collections, paginated and sorted
/// by printing name. Owner identity is resolved by the host adapter and passed in.
/// </summary>
public sealed class MyCardsService
{
    private const int DefaultLimit = 50;
    private const int MaxLimit = 200;

    private readonly IDocumentSession _session;
    private readonly CardInstanceHydrator _hydrator;

    public MyCardsService(IDocumentSession session, CardInstanceHydrator hydrator)
    {
        _session = session;
        _hydrator = hydrator;
    }

    public async Task<CardInstanceListResponse> ListAsync(Guid ownerId, int? take, int? skip, CancellationToken ct)
    {
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
            return new CardInstanceListResponse
            {
                Cards = Array.Empty<CardInstanceResponse>(),
                Total = 0,
            };
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

        return new CardInstanceListResponse { Cards = hydrated, Total = total };
    }
}
