using LupiraMtgApi.Data;
using LupiraMtgApi.Domain.Collection;
using LupiraMtgApi.Models.Collections;
using Marten;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace LupiraMtgApi.Handlers;

public sealed class CollectionsHandler
{
    private const int MaxNameLength = 64;

    private readonly IDocumentSession _session;
    private readonly LupiraMtgDbContext _db;
    private readonly CardInstanceHydrator _hydrator;

    public CollectionsHandler(
        IDocumentSession session,
        LupiraMtgDbContext db,
        CardInstanceHydrator hydrator)
    {
        _session = session;
        _db = db;
        _hydrator = hydrator;
    }

    public async Task<Results<Ok<CollectionListResponse>, UnauthorizedHttpResult>> ListAsync(
        HttpContext httpContext,
        CancellationToken ct)
    {
        if (!httpContext.TryGetOwnerSub(out var sub))
        {
            return TypedResults.Unauthorized();
        }

        var docs = await Marten.QueryableExtensions.ToListAsync(
            _session.Query<CollectionDocument>()
                .Where(c => c.OwnerSub == sub && !c.Removed)
                .OrderBy(c => c.Name),
            ct);

        var responses = docs
            .Select(d => new CollectionResponse
            {
                Id = d.Id,
                Name = d.Name,
                CardCount = d.Cards.Count,
                CreatedAt = d.CreatedAt,
                UpdatedAt = d.UpdatedAt,
            })
            .ToList();

        return TypedResults.Ok(new CollectionListResponse { Collections = responses });
    }

    public async Task<Results<Ok<CollectionResponse>, BadRequest<string>, UnauthorizedHttpResult>> CreateAsync(
        HttpContext httpContext,
        CreateCollectionRequest request,
        CancellationToken ct)
    {
        if (!httpContext.TryGetOwnerSub(out var sub))
        {
            return TypedResults.Unauthorized();
        }

        var name = request.Name?.Trim();
        if (string.IsNullOrEmpty(name) || name.Length > MaxNameLength)
        {
            return TypedResults.BadRequest($"Name must be 1..{MaxNameLength} characters.");
        }

        var now = DateTimeOffset.UtcNow;
        var doc = new CollectionDocument
        {
            Id = Guid.NewGuid(),
            OwnerSub = sub,
            Name = name,
            Removed = false,
            CreatedAt = now,
            UpdatedAt = now,
        };

        _session.Store(doc);
        await _session.SaveChangesAsync(ct);

        return TypedResults.Ok(new CollectionResponse
        {
            Id = doc.Id,
            Name = doc.Name,
            CardCount = 0,
            CreatedAt = doc.CreatedAt,
            UpdatedAt = doc.UpdatedAt,
        });
    }

    public async Task<Results<Ok<CollectionDetailResponse>, NotFound, UnauthorizedHttpResult>> GetAsync(
        HttpContext httpContext,
        Guid collectionId,
        CancellationToken ct)
    {
        if (!httpContext.TryGetOwnerSub(out var sub))
        {
            return TypedResults.Unauthorized();
        }

        var doc = await this.LoadOwnedAsync(collectionId, sub, ct);
        if (doc is null)
        {
            return TypedResults.NotFound();
        }

        var cards = await _hydrator.HydrateAsync(doc.Cards, doc.Id, doc.Name, ct);

        return TypedResults.Ok(new CollectionDetailResponse
        {
            Id = doc.Id,
            Name = doc.Name,
            Cards = cards,
            CreatedAt = doc.CreatedAt,
            UpdatedAt = doc.UpdatedAt,
        });
    }

    public async Task<Results<Ok<CollectionResponse>, NotFound, BadRequest<string>, UnauthorizedHttpResult>> RenameAsync(
        HttpContext httpContext,
        Guid collectionId,
        RenameCollectionRequest request,
        CancellationToken ct)
    {
        if (!httpContext.TryGetOwnerSub(out var sub))
        {
            return TypedResults.Unauthorized();
        }

        var name = request.Name?.Trim();
        if (string.IsNullOrEmpty(name) || name.Length > MaxNameLength)
        {
            return TypedResults.BadRequest($"Name must be 1..{MaxNameLength} characters.");
        }

        var doc = await this.LoadOwnedAsync(collectionId, sub, ct);
        if (doc is null)
        {
            return TypedResults.NotFound();
        }

        doc.Name = name;
        doc.UpdatedAt = DateTimeOffset.UtcNow;
        _session.Store(doc);
        await _session.SaveChangesAsync(ct);

        return TypedResults.Ok(new CollectionResponse
        {
            Id = doc.Id,
            Name = doc.Name,
            CardCount = doc.Cards.Count,
            CreatedAt = doc.CreatedAt,
            UpdatedAt = doc.UpdatedAt,
        });
    }

    public async Task<Results<NoContent, NotFound, UnauthorizedHttpResult>> DeleteAsync(
        HttpContext httpContext,
        Guid collectionId,
        CancellationToken ct)
    {
        if (!httpContext.TryGetOwnerSub(out var sub))
        {
            return TypedResults.Unauthorized();
        }

        var doc = await this.LoadOwnedAsync(collectionId, sub, ct);
        if (doc is null)
        {
            return TypedResults.NotFound();
        }

        doc.Removed = true;
        doc.UpdatedAt = DateTimeOffset.UtcNow;
        _session.Store(doc);
        await _session.SaveChangesAsync(ct);

        return TypedResults.NoContent();
    }

    public async Task<Results<Ok<CardListResponse>, NotFound, UnauthorizedHttpResult>> ListCardsAsync(
        HttpContext httpContext,
        Guid collectionId,
        CancellationToken ct)
    {
        if (!httpContext.TryGetOwnerSub(out var sub))
        {
            return TypedResults.Unauthorized();
        }

        var doc = await this.LoadOwnedAsync(collectionId, sub, ct);
        if (doc is null)
        {
            return TypedResults.NotFound();
        }

        var cards = await _hydrator.HydrateAsync(doc.Cards, doc.Id, doc.Name, ct);
        return TypedResults.Ok(new CardListResponse { Cards = cards });
    }

    public async Task<Results<Ok<CardInstanceResponse>, NotFound, BadRequest<string>, UnauthorizedHttpResult>> AddCardAsync(
        HttpContext httpContext,
        Guid collectionId,
        AddCardToCollectionRequest request,
        CancellationToken ct)
    {
        if (!httpContext.TryGetOwnerSub(out var sub))
        {
            return TypedResults.Unauthorized();
        }

        var doc = await this.LoadOwnedAsync(collectionId, sub, ct);
        if (doc is null)
        {
            return TypedResults.NotFound();
        }

        var printing = await Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions.FirstOrDefaultAsync(
            _db.CardPrintings.AsNoTracking(),
            p => p.Id == request.PrintingId,
            ct);
        if (printing is null)
        {
            return TypedResults.BadRequest("Unknown printing id.");
        }

        var instance = new CardInstance
        {
            InstanceId = Guid.NewGuid(),
            PrintingId = request.PrintingId,
            Foil = request.Foil,
            Language = string.IsNullOrEmpty(request.Language) ? "en" : request.Language,
            Condition = string.IsNullOrEmpty(request.Condition) ? "NM" : request.Condition,
            AcquiredAt = DateTimeOffset.UtcNow,
        };

        doc.Cards.Add(instance);
        doc.UpdatedAt = DateTimeOffset.UtcNow;
        _session.Store(doc);
        await _session.SaveChangesAsync(ct);

        var hydrated = await _hydrator.HydrateAsync(new[] { instance }, doc.Id, doc.Name, ct);
        return TypedResults.Ok(hydrated.Single());
    }

    public async Task<Results<NoContent, NotFound, UnauthorizedHttpResult>> RemoveCardAsync(
        HttpContext httpContext,
        Guid collectionId,
        Guid instanceId,
        CancellationToken ct)
    {
        if (!httpContext.TryGetOwnerSub(out var sub))
        {
            return TypedResults.Unauthorized();
        }

        var doc = await this.LoadOwnedAsync(collectionId, sub, ct);
        if (doc is null)
        {
            return TypedResults.NotFound();
        }

        var removed = doc.Cards.RemoveAll(c => c.InstanceId == instanceId);
        if (removed == 0)
        {
            return TypedResults.NotFound();
        }

        doc.UpdatedAt = DateTimeOffset.UtcNow;
        _session.Store(doc);
        await _session.SaveChangesAsync(ct);
        return TypedResults.NoContent();
    }

    public async Task<Results<Ok<CardInstanceResponse>, NotFound, BadRequest<string>, UnauthorizedHttpResult>> MoveCardAsync(
        HttpContext httpContext,
        Guid collectionId,
        Guid instanceId,
        MoveCardRequest request,
        CancellationToken ct)
    {
        if (!httpContext.TryGetOwnerSub(out var sub))
        {
            return TypedResults.Unauthorized();
        }

        if (request.ToCollectionId == collectionId)
        {
            return TypedResults.BadRequest("Source and destination collections are the same.");
        }

        var source = await this.LoadOwnedAsync(collectionId, sub, ct);
        if (source is null)
        {
            return TypedResults.NotFound();
        }

        var destination = await this.LoadOwnedAsync(request.ToCollectionId, sub, ct);
        if (destination is null)
        {
            return TypedResults.NotFound();
        }

        var card = source.Cards.FirstOrDefault(c => c.InstanceId == instanceId);
        if (card is null)
        {
            return TypedResults.NotFound();
        }

        source.Cards.RemoveAll(c => c.InstanceId == instanceId);
        destination.Cards.Add(card);
        var now = DateTimeOffset.UtcNow;
        source.UpdatedAt = now;
        destination.UpdatedAt = now;

        _session.Store(source);
        _session.Store(destination);
        await _session.SaveChangesAsync(ct);

        var hydrated = await _hydrator.HydrateAsync(new[] { card }, destination.Id, destination.Name, ct);
        return TypedResults.Ok(hydrated.Single());
    }

    private async Task<CollectionDocument?> LoadOwnedAsync(Guid id, string ownerSub, CancellationToken ct)
    {
        var doc = await _session.LoadAsync<CollectionDocument>(id, ct);
        if (doc is null || doc.OwnerSub != ownerSub || doc.Removed)
        {
            return null;
        }

        return doc;
    }
}
