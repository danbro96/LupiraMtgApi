using LupiraMtgApi.Domain.Collection;
using LupiraMtgApi.Models.Collections;
using Marten;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;

namespace LupiraMtgApi.Handlers;

public sealed class MyCardsHandler
{
    private readonly IDocumentSession session;
    private readonly CardInstanceHydrator hydrator;

    public MyCardsHandler(IDocumentSession session, CardInstanceHydrator hydrator)
    {
        this.session = session;
        this.hydrator = hydrator;
    }

    public async Task<Results<Ok<CardListResponse>, UnauthorizedHttpResult>> ListAsync(
        HttpContext httpContext,
        CancellationToken ct)
    {
        if (!httpContext.TryGetOwnerSub(out var sub))
        {
            return TypedResults.Unauthorized();
        }

        var collections = await Marten.QueryableExtensions.ToListAsync(
            this.session.Query<CollectionDocument>()
                .Where(c => c.OwnerSub == sub && !c.Removed),
            ct);

        var allCards = collections.SelectMany(c => c.Cards).ToList();
        if (allCards.Count == 0)
        {
            return TypedResults.Ok(new CardListResponse { Cards = Array.Empty<CardInstanceResponse>() });
        }

        var collectionByInstance = collections
            .SelectMany(c => c.Cards.Select(card => (Instance: card, Collection: c)))
            .ToDictionary(x => x.Instance.InstanceId, x => x.Collection);

        var hydrated = await this.hydrator.HydrateAsync(allCards, collectionId: null, collectionName: null, ct);
        foreach (var card in hydrated)
        {
            if (collectionByInstance.TryGetValue(card.InstanceId, out var owner))
            {
                card.CollectionId = owner.Id;
                card.CollectionName = owner.Name;
            }
        }

        hydrated.Sort((a, b) => string.Compare(a.Printing.Name, b.Printing.Name, StringComparison.OrdinalIgnoreCase));
        return TypedResults.Ok(new CardListResponse { Cards = hydrated });
    }
}
