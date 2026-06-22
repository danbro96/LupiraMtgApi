using LupiraMtgApi.Catalog.Data;
using LupiraMtgApi.Collections.Domain;
using LupiraMtgApi.Collections.Dtos;
using LupiraMtgApi.Collections.Mappers;
using Marten;
using Microsoft.EntityFrameworkCore;

namespace LupiraMtgApi.Collections.Application;

/// <summary>
/// Collection CRUD + card-instance management. Owner identity is resolved by the host adapter and
/// passed in; methods return <see cref="Op{T}"/> / nullable / bool which the host maps to HTTP.
/// </summary>
public sealed class CollectionsService
{
    private const int MaxNameLength = 64;
    private const int PageDefaultLimit = 50;
    private const int PageMaxLimit = 200;

    private readonly IDocumentSession _session;
    private readonly LupiraMtgDbContext _db;
    private readonly CardInstanceHydrator _hydrator;

    public CollectionsService(
        IDocumentSession session,
        LupiraMtgDbContext db,
        CardInstanceHydrator hydrator)
    {
        _session = session;
        _db = db;
        _hydrator = hydrator;
    }

    public async Task<CollectionListResponse> ListAsync(string ownerId, CancellationToken ct)
    {
        var docs = await Marten.QueryableExtensions.ToListAsync(
            _session.Query<CollectionDocument>()
                .Where(c => c.OwnerId == ownerId && !c.IsRemoved)
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

        return new CollectionListResponse { Collections = responses };
    }

    public async Task<Op<CollectionResponse>> CreateAsync(string ownerId, CreateCollectionRequest request, CancellationToken ct)
    {
        var name = request.Name?.Trim();
        if (string.IsNullOrEmpty(name) || name.Length > MaxNameLength)
        {
            return Op<CollectionResponse>.Invalid($"Name must be 1..{MaxNameLength} characters.");
        }

        var now = DateTimeOffset.UtcNow;
        var doc = new CollectionDocument
        {
            Id = Guid.NewGuid(),
            OwnerId = ownerId,
            Name = name,
            IsRemoved = false,
            CreatedAt = now,
            UpdatedAt = now,
        };

        _session.Store(doc);
        await _session.SaveChangesAsync(ct);

        return Op<CollectionResponse>.Ok(new CollectionResponse
        {
            Id = doc.Id,
            Name = doc.Name,
            CardCount = 0,
            CreatedAt = doc.CreatedAt,
            UpdatedAt = doc.UpdatedAt,
        });
    }

    public async Task<CollectionDetailResponse?> GetAsync(string ownerId, Guid collectionId, CancellationToken ct)
    {
        var doc = await this.LoadOwnedAsync(collectionId, ownerId, ct);
        if (doc is null)
        {
            return null;
        }

        var cards = await _hydrator.HydrateAsync(doc.Cards, doc.Id, doc.Name, ct);

        return new CollectionDetailResponse
        {
            Id = doc.Id,
            Name = doc.Name,
            Cards = cards,
            CreatedAt = doc.CreatedAt,
            UpdatedAt = doc.UpdatedAt,
        };
    }

    public async Task<Op<CollectionResponse>> RenameAsync(string ownerId, Guid collectionId, RenameCollectionRequest request, CancellationToken ct)
    {
        var name = request.Name?.Trim();
        if (string.IsNullOrEmpty(name) || name.Length > MaxNameLength)
        {
            return Op<CollectionResponse>.Invalid($"Name must be 1..{MaxNameLength} characters.");
        }

        var doc = await this.LoadOwnedAsync(collectionId, ownerId, ct);
        if (doc is null)
        {
            return Op<CollectionResponse>.NotFound();
        }

        doc.Name = name;
        doc.UpdatedAt = DateTimeOffset.UtcNow;
        _session.Store(doc);
        await _session.SaveChangesAsync(ct);

        return Op<CollectionResponse>.Ok(new CollectionResponse
        {
            Id = doc.Id,
            Name = doc.Name,
            CardCount = doc.Cards.Count,
            CreatedAt = doc.CreatedAt,
            UpdatedAt = doc.UpdatedAt,
        });
    }

    public async Task<bool> DeleteAsync(string ownerId, Guid collectionId, CancellationToken ct)
    {
        var doc = await this.LoadOwnedAsync(collectionId, ownerId, ct);
        if (doc is null)
        {
            return false;
        }

        doc.IsRemoved = true;
        doc.UpdatedAt = DateTimeOffset.UtcNow;
        _session.Store(doc);
        await _session.SaveChangesAsync(ct);

        return true;
    }

    public async Task<CardInstanceListResponse?> ListCardsAsync(string ownerId, Guid collectionId, int? take, int? skip, CancellationToken ct)
    {
        var clampedTake = Math.Clamp(take ?? PageDefaultLimit, 1, PageMaxLimit);
        var clampedSkip = Math.Max(skip ?? 0, 0);

        var doc = await this.LoadOwnedAsync(collectionId, ownerId, ct);
        if (doc is null)
        {
            return null;
        }

        var total = doc.Cards.Count;
        if (total == 0)
        {
            return new CardInstanceListResponse
            {
                Cards = Array.Empty<CardInstanceResponse>(),
                Total = 0,
            };
        }

        var nameByPrintingId = await _hydrator.LoadPrintingNamesAsync(
            doc.Cards.Select(c => c.PrintingId),
            ct);

        var page = doc.Cards
            .OrderBy(c => nameByPrintingId.GetValueOrDefault(c.PrintingId, string.Empty), StringComparer.OrdinalIgnoreCase)
            .ThenBy(c => c.InstanceId)
            .Skip(clampedSkip)
            .Take(clampedTake)
            .ToList();

        var cards = await _hydrator.HydrateAsync(page, doc.Id, doc.Name, ct);
        return new CardInstanceListResponse { Cards = cards, Total = total };
    }

    public async Task<Op<CardInstanceResponse>> AddCardAsync(string ownerId, Guid collectionId, AddCardToCollectionRequest request, CancellationToken ct)
    {
        var doc = await this.LoadOwnedAsync(collectionId, ownerId, ct);
        if (doc is null)
        {
            return Op<CardInstanceResponse>.NotFound();
        }

        var printing = await Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions.FirstOrDefaultAsync(
            _db.CardPrintings.AsNoTracking(),
            p => p.Id == request.PrintingId,
            ct);
        if (printing is null)
        {
            return Op<CardInstanceResponse>.Invalid("Unknown printing id.");
        }

        var instance = new CardInstance
        {
            InstanceId = Guid.NewGuid(),
            PrintingId = request.PrintingId,
            IsFoil = request.IsFoil,
            Language = string.IsNullOrEmpty(request.Language) ? "en" : request.Language,
            Condition = string.IsNullOrEmpty(request.Condition) ? "NM" : request.Condition,
            AcquiredAt = DateTimeOffset.UtcNow,
        };

        doc.Cards.Add(instance);
        doc.UpdatedAt = DateTimeOffset.UtcNow;
        _session.Store(doc);
        await _session.SaveChangesAsync(ct);

        var hydrated = await _hydrator.HydrateAsync(new[] { instance }, doc.Id, doc.Name, ct);
        return Op<CardInstanceResponse>.Ok(hydrated.Single());
    }

    public async Task<bool> RemoveCardAsync(string ownerId, Guid collectionId, Guid instanceId, CancellationToken ct)
    {
        var doc = await this.LoadOwnedAsync(collectionId, ownerId, ct);
        if (doc is null)
        {
            return false;
        }

        var removed = doc.Cards.RemoveAll(c => c.InstanceId == instanceId);
        if (removed == 0)
        {
            return false;
        }

        doc.UpdatedAt = DateTimeOffset.UtcNow;
        _session.Store(doc);
        await _session.SaveChangesAsync(ct);
        return true;
    }

    public async Task<Op<CardInstanceResponse>> MoveCardAsync(string ownerId, Guid collectionId, Guid instanceId, MoveCardRequest request, CancellationToken ct)
    {
        if (request.ToCollectionId == collectionId)
        {
            return Op<CardInstanceResponse>.Invalid("Source and destination collections are the same.");
        }

        var source = await this.LoadOwnedAsync(collectionId, ownerId, ct);
        if (source is null)
        {
            return Op<CardInstanceResponse>.NotFound();
        }

        var destination = await this.LoadOwnedAsync(request.ToCollectionId, ownerId, ct);
        if (destination is null)
        {
            return Op<CardInstanceResponse>.NotFound();
        }

        var card = source.Cards.FirstOrDefault(c => c.InstanceId == instanceId);
        if (card is null)
        {
            return Op<CardInstanceResponse>.NotFound();
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
        return Op<CardInstanceResponse>.Ok(hydrated.Single());
    }

    public async Task<Op<BulkAddCardsResponse>> BulkAddCardsAsync(string ownerId, Guid collectionId, BulkAddCardsRequest request, CancellationToken ct)
    {
        if (request.Items is null || request.Items.Count == 0)
        {
            return Op<BulkAddCardsResponse>.Invalid("`items` must contain at least one entry.");
        }

        // Expand each item × Count into a flat list of (printingId, instance template).
        // Cap total instances per call to keep response sizes bounded.
        const int MaxInstancesPerCall = 500;
        const int MaxItemCount = 50;

        var expanded = new List<(BulkAddCardItem item, int count)>(request.Items.Count);
        var totalInstances = 0;
        foreach (var item in request.Items)
        {
            if (string.IsNullOrWhiteSpace(item.PrintingId))
            {
                return Op<BulkAddCardsResponse>.Invalid("Every item must have a `printingId`.");
            }
            var count = Math.Clamp(item.Count ?? 1, 1, MaxItemCount);
            totalInstances += count;
            expanded.Add((item, count));
        }
        if (totalInstances > MaxInstancesPerCall)
        {
            return Op<BulkAddCardsResponse>.Invalid($"Bulk add limited to {MaxInstancesPerCall} instances per call (got {totalInstances}).");
        }

        var doc = await this.LoadOwnedAsync(collectionId, ownerId, ct);
        if (doc is null)
        {
            return Op<BulkAddCardsResponse>.NotFound();
        }

        // Validate every printing exists in one batch.
        var distinctIds = expanded.Select(x => x.item.PrintingId).Distinct().ToList();
        var knownIds = await Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions.ToListAsync(
            _db.CardPrintings
                .AsNoTracking()
                .Where(p => distinctIds.Contains(p.Id))
                .Select(p => p.Id),
            ct);
        var unknown = distinctIds.Except(knownIds).ToList();
        if (unknown.Count > 0)
        {
            return Op<BulkAddCardsResponse>.Invalid($"Unknown printing ids: {string.Join(", ", unknown)}");
        }

        var now = DateTimeOffset.UtcNow;
        var created = new List<CardInstance>(totalInstances);
        foreach (var (item, count) in expanded)
        {
            for (var i = 0; i < count; i++)
            {
                var instance = new CardInstance
                {
                    InstanceId = Guid.NewGuid(),
                    PrintingId = item.PrintingId,
                    IsFoil = item.IsFoil,
                    Language = string.IsNullOrEmpty(item.Language) ? "en" : item.Language,
                    Condition = string.IsNullOrEmpty(item.Condition) ? "NM" : item.Condition,
                    AcquiredAt = now,
                };
                doc.Cards.Add(instance);
                created.Add(instance);
            }
        }

        doc.UpdatedAt = now;
        _session.Store(doc);
        await _session.SaveChangesAsync(ct);

        var hydrated = await _hydrator.HydrateAsync(created, doc.Id, doc.Name, ct);
        return Op<BulkAddCardsResponse>.Ok(new BulkAddCardsResponse { Added = hydrated });
    }

    public async Task<Op<BulkRemoveCardsResponse>> BulkRemoveCardsAsync(string ownerId, Guid collectionId, BulkRemoveCardsRequest request, CancellationToken ct)
    {
        if (request.InstanceIds is null || request.InstanceIds.Count == 0)
        {
            return Op<BulkRemoveCardsResponse>.Invalid("`instanceIds` must contain at least one id.");
        }

        var doc = await this.LoadOwnedAsync(collectionId, ownerId, ct);
        if (doc is null)
        {
            return Op<BulkRemoveCardsResponse>.NotFound();
        }

        var requested = request.InstanceIds.Distinct().ToHashSet();
        var existing = doc.Cards.Where(c => requested.Contains(c.InstanceId)).Select(c => c.InstanceId).ToHashSet();
        var removed = doc.Cards.RemoveAll(c => existing.Contains(c.InstanceId));
        var missing = requested.Count - existing.Count;

        if (removed > 0)
        {
            doc.UpdatedAt = DateTimeOffset.UtcNow;
            _session.Store(doc);
            await _session.SaveChangesAsync(ct);
        }

        return Op<BulkRemoveCardsResponse>.Ok(new BulkRemoveCardsResponse { RemovedCount = removed, MissingCount = missing });
    }

    public async Task<Op<BulkMoveCardsResponse>> BulkMoveCardsAsync(string ownerId, Guid collectionId, BulkMoveCardsRequest request, CancellationToken ct)
    {
        if (request.InstanceIds is null || request.InstanceIds.Count == 0)
        {
            return Op<BulkMoveCardsResponse>.Invalid("`instanceIds` must contain at least one id.");
        }

        if (request.ToCollectionId == collectionId)
        {
            return Op<BulkMoveCardsResponse>.Invalid("Source and destination collections are the same.");
        }

        var source = await this.LoadOwnedAsync(collectionId, ownerId, ct);
        if (source is null)
        {
            return Op<BulkMoveCardsResponse>.NotFound();
        }

        var destination = await this.LoadOwnedAsync(request.ToCollectionId, ownerId, ct);
        if (destination is null)
        {
            return Op<BulkMoveCardsResponse>.NotFound();
        }

        var requested = request.InstanceIds.Distinct().ToHashSet();
        var moved = source.Cards.Where(c => requested.Contains(c.InstanceId)).ToList();
        var missing = requested.Count - moved.Count;

        if (moved.Count > 0)
        {
            source.Cards.RemoveAll(c => requested.Contains(c.InstanceId));
            destination.Cards.AddRange(moved);
            var now = DateTimeOffset.UtcNow;
            source.UpdatedAt = now;
            destination.UpdatedAt = now;
            _session.Store(source);
            _session.Store(destination);
            await _session.SaveChangesAsync(ct);
        }

        var hydrated = await _hydrator.HydrateAsync(moved, destination.Id, destination.Name, ct);
        return Op<BulkMoveCardsResponse>.Ok(new BulkMoveCardsResponse { Moved = hydrated, MissingCount = missing });
    }

    private async Task<CollectionDocument?> LoadOwnedAsync(Guid id, string ownerId, CancellationToken ct)
    {
        var doc = await _session.LoadAsync<CollectionDocument>(id, ct);
        if (doc is null || doc.OwnerId != ownerId || doc.IsRemoved)
        {
            return null;
        }

        return doc;
    }
}
