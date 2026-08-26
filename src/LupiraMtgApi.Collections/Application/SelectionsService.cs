using LupiraMtgApi.Catalog.Data;
using LupiraMtgApi.Collections.Domain;
using LupiraMtgApi.Collections.Dtos;
using LupiraMtgApi.Collections.Mappers;
using Marten;
using Microsoft.EntityFrameworkCore;

namespace LupiraMtgApi.Collections.Application;

/// <summary>
/// Ephemeral selection (scan-staging) management and commit-into-collection. Owner identity is
/// resolved by the host adapter and passed in.
/// </summary>
public sealed class SelectionsService
{
    private static readonly TimeSpan DefaultTtl = TimeSpan.FromDays(7);

    private readonly IDocumentSession _session;
    private readonly LupiraMtgDbContext _db;
    private readonly CardInstanceHydrator _hydrator;

    public SelectionsService(
        IDocumentSession session,
        LupiraMtgDbContext db,
        CardInstanceHydrator hydrator)
    {
        _session = session;
        _db = db;
        _hydrator = hydrator;
    }

    public async Task<SelectionResponse> CreateAsync(string ownerId, CancellationToken ct)
    {
        var now = DateTimeOffset.UtcNow;
        var doc = new SelectionDocument
        {
            Id = Guid.NewGuid(),
            OwnerId = ownerId,
            Cards = new List<SelectionEntry>(),
            CreatedAt = now,
            ExpiresAt = now.Add(DefaultTtl),
        };

        _session.Store(doc);
        await _session.SaveChangesAsync(ct);

        return await this.MapAsync(doc, ct);
    }

    public async Task<SelectionResponse?> GetAsync(string ownerId, Guid selectionId, CancellationToken ct)
    {
        var doc = await this.LoadOwnedAsync(selectionId, ownerId, ct);
        if (doc is null)
        {
            return null;
        }

        return await this.MapAsync(doc, ct);
    }

    public async Task<Op<SelectionEntryDto>> AddCardAsync(string ownerId, Guid selectionId, AddSelectionEntryRequest request, CancellationToken ct)
    {
        var doc = await this.LoadOwnedAsync(selectionId, ownerId, ct);
        if (doc is null)
        {
            return Op<SelectionEntryDto>.NotFound();
        }

        var printing = await Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions.FirstOrDefaultAsync(
            _db.CardPrintings.AsNoTracking(),
            p => p.Id == request.PrintingId,
            ct);
        if (printing is null)
        {
            return Op<SelectionEntryDto>.Invalid("Unknown printing id.");
        }

        var language = string.IsNullOrEmpty(request.Language) ? "en" : request.Language;
        var condition = string.IsNullOrEmpty(request.Condition) ? "NM" : request.Condition;

        if (!request.AllowDuplicate)
        {
            var clash = doc.Cards.Any(c =>
                c.PrintingId == request.PrintingId &&
                c.IsFoil == request.IsFoil &&
                string.Equals(c.Language, language, StringComparison.OrdinalIgnoreCase));
            if (clash)
            {
                return Op<SelectionEntryDto>.Conflict("Already in selection. Pass allowDuplicate=true to add another copy.");
            }
        }

        var entry = new SelectionEntry
        {
            InstanceId = Guid.NewGuid(),
            PrintingId = request.PrintingId,
            IsFoil = request.IsFoil,
            Language = language,
            Condition = condition,
            Confidence = Math.Clamp(request.Confidence, 0.0, 1.0),
        };

        doc.Cards.Add(entry);
        _session.Store(doc);
        await _session.SaveChangesAsync(ct);

        var hydrated = await _hydrator.HydrateSelectionAsync(new[] { entry }, ct);
        return Op<SelectionEntryDto>.Ok(hydrated.Single());
    }

    public async Task<bool> RemoveCardAsync(string ownerId, Guid selectionId, Guid instanceId, CancellationToken ct)
    {
        var doc = await this.LoadOwnedAsync(selectionId, ownerId, ct);
        if (doc is null)
        {
            return false;
        }

        var removed = doc.Cards.RemoveAll(c => c.InstanceId == instanceId);
        if (removed == 0)
        {
            return false;
        }

        _session.Store(doc);
        await _session.SaveChangesAsync(ct);
        return true;
    }

    public async Task<Op<CommitSelectionResponse>> CommitAsync(string ownerId, Guid selectionId, CommitSelectionRequest request, CancellationToken ct)
    {
        var selection = await this.LoadOwnedAsync(selectionId, ownerId, ct);
        if (selection is null)
        {
            return Op<CommitSelectionResponse>.NotFound();
        }

        var collection = await _session.LoadAsync<CollectionDocument>(request.CollectionId, ct);
        if (collection is null || collection.OwnerId != ownerId || collection.IsRemoved)
        {
            return Op<CommitSelectionResponse>.NotFound();
        }

        var pickIds = request.InstanceIds is { Count: > 0 } ? request.InstanceIds.ToHashSet() : null;
        var picked = pickIds is null
            ? selection.Cards.ToList()
            : selection.Cards.Where(c => pickIds.Contains(c.InstanceId)).ToList();

        if (picked.Count == 0)
        {
            return Op<CommitSelectionResponse>.Invalid("No selection entries match the requested instance IDs.");
        }

        var now = DateTimeOffset.UtcNow;
        foreach (var entry in picked)
        {
            collection.Cards.Add(new CardInstance
            {
                InstanceId = entry.InstanceId,
                PrintingId = entry.PrintingId,
                IsFoil = entry.IsFoil,
                Language = entry.Language,
                Condition = entry.Condition,
                AcquiredAt = now,
            });
        }

        var pickedIds = picked.Select(p => p.InstanceId).ToHashSet();
        selection.Cards.RemoveAll(c => pickedIds.Contains(c.InstanceId));
        collection.UpdatedAt = now;

        _session.Store(collection);
        _session.Store(selection);
        await _session.SaveChangesAsync(ct);

        return Op<CommitSelectionResponse>.Ok(new CommitSelectionResponse
        {
            CollectionId = collection.Id,
            CollectionName = collection.Name,
            AddedCount = picked.Count,
            RemainingCount = selection.Cards.Count,
        });
    }

    private async Task<SelectionDocument?> LoadOwnedAsync(Guid id, string ownerId, CancellationToken ct)
    {
        var doc = await _session.LoadAsync<SelectionDocument>(id, ct);
        if (doc is null || doc.OwnerId != ownerId || doc.ExpiresAt < DateTimeOffset.UtcNow)
        {
            return null;
        }

        return doc;
    }

    private async Task<SelectionResponse> MapAsync(SelectionDocument doc, CancellationToken ct)
    {
        var cards = await _hydrator.HydrateSelectionAsync(doc.Cards, ct);
        return new SelectionResponse
        {
            Id = doc.Id,
            Cards = cards,
            CreatedAt = doc.CreatedAt,
            ExpiresAt = doc.ExpiresAt,
        };
    }
}
