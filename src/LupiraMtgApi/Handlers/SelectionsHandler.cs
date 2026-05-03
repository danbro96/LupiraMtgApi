using LupiraMtgApi.Data;
using LupiraMtgApi.Domain.Collection;
using LupiraMtgApi.Domain.Selection;
using LupiraMtgApi.Models.Selections;
using Marten;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace LupiraMtgApi.Handlers;

public sealed class SelectionsHandler
{
    private static readonly TimeSpan DefaultTtl = TimeSpan.FromDays(7);

    private readonly IDocumentSession session;
    private readonly LupiraMtgDbContext db;
    private readonly CardInstanceHydrator hydrator;

    public SelectionsHandler(
        IDocumentSession session,
        LupiraMtgDbContext db,
        CardInstanceHydrator hydrator)
    {
        this.session = session;
        this.db = db;
        this.hydrator = hydrator;
    }

    public async Task<Results<Ok<SelectionResponse>, UnauthorizedHttpResult>> CreateAsync(
        HttpContext httpContext,
        CancellationToken ct)
    {
        if (!httpContext.TryGetOwnerSub(out var sub))
        {
            return TypedResults.Unauthorized();
        }

        var now = DateTimeOffset.UtcNow;
        var doc = new SelectionDocument
        {
            Id = Guid.NewGuid(),
            OwnerSub = sub,
            Cards = new List<SelectionEntry>(),
            CreatedAt = now,
            ExpiresAt = now.Add(DefaultTtl),
        };

        this.session.Store(doc);
        await this.session.SaveChangesAsync(ct);

        return TypedResults.Ok(await this.MapAsync(doc, ct));
    }

    public async Task<Results<Ok<SelectionResponse>, NotFound, UnauthorizedHttpResult>> GetAsync(
        HttpContext httpContext,
        Guid selectionId,
        CancellationToken ct)
    {
        if (!httpContext.TryGetOwnerSub(out var sub))
        {
            return TypedResults.Unauthorized();
        }

        var doc = await this.LoadOwnedAsync(selectionId, sub, ct);
        if (doc is null)
        {
            return TypedResults.NotFound();
        }

        return TypedResults.Ok(await this.MapAsync(doc, ct));
    }

    public async Task<Results<Ok<SelectionEntryResponse>, NotFound, BadRequest<string>, Conflict<string>, UnauthorizedHttpResult>> AddCardAsync(
        HttpContext httpContext,
        Guid selectionId,
        AddSelectionEntryRequest request,
        CancellationToken ct)
    {
        if (!httpContext.TryGetOwnerSub(out var sub))
        {
            return TypedResults.Unauthorized();
        }

        var doc = await this.LoadOwnedAsync(selectionId, sub, ct);
        if (doc is null)
        {
            return TypedResults.NotFound();
        }

        var printing = await Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions.FirstOrDefaultAsync(
            this.db.CardPrintings.AsNoTracking(),
            p => p.Id == request.PrintingId,
            ct);
        if (printing is null)
        {
            return TypedResults.BadRequest("Unknown printing id.");
        }

        var language = string.IsNullOrEmpty(request.Language) ? "en" : request.Language;
        var condition = string.IsNullOrEmpty(request.Condition) ? "NM" : request.Condition;

        if (!request.AllowDuplicate)
        {
            var clash = doc.Cards.Any(c =>
                c.PrintingId == request.PrintingId &&
                c.Foil == request.Foil &&
                string.Equals(c.Language, language, StringComparison.OrdinalIgnoreCase));
            if (clash)
            {
                return TypedResults.Conflict("Already in selection. Pass allowDuplicate=true to add another copy.");
            }
        }

        var entry = new SelectionEntry
        {
            InstanceId = Guid.NewGuid(),
            PrintingId = request.PrintingId,
            Foil = request.Foil,
            Language = language,
            Condition = condition,
            Confidence = Math.Clamp(request.Confidence, 0.0, 1.0),
        };

        doc.Cards.Add(entry);
        this.session.Store(doc);
        await this.session.SaveChangesAsync(ct);

        var hydrated = await this.hydrator.HydrateSelectionAsync(new[] { entry }, ct);
        return TypedResults.Ok(hydrated.Single());
    }

    public async Task<Results<NoContent, NotFound, UnauthorizedHttpResult>> RemoveCardAsync(
        HttpContext httpContext,
        Guid selectionId,
        Guid instanceId,
        CancellationToken ct)
    {
        if (!httpContext.TryGetOwnerSub(out var sub))
        {
            return TypedResults.Unauthorized();
        }

        var doc = await this.LoadOwnedAsync(selectionId, sub, ct);
        if (doc is null)
        {
            return TypedResults.NotFound();
        }

        var removed = doc.Cards.RemoveAll(c => c.InstanceId == instanceId);
        if (removed == 0)
        {
            return TypedResults.NotFound();
        }

        this.session.Store(doc);
        await this.session.SaveChangesAsync(ct);
        return TypedResults.NoContent();
    }

    public async Task<Results<Ok<CommitSelectionResponse>, NotFound, BadRequest<string>, UnauthorizedHttpResult>> CommitAsync(
        HttpContext httpContext,
        Guid selectionId,
        CommitSelectionRequest request,
        CancellationToken ct)
    {
        if (!httpContext.TryGetOwnerSub(out var sub))
        {
            return TypedResults.Unauthorized();
        }

        var selection = await this.LoadOwnedAsync(selectionId, sub, ct);
        if (selection is null)
        {
            return TypedResults.NotFound();
        }

        var collection = await this.session.LoadAsync<CollectionDocument>(request.CollectionId, ct);
        if (collection is null || collection.OwnerSub != sub || collection.Removed)
        {
            return TypedResults.NotFound();
        }

        var pickIds = request.InstanceIds is { Count: > 0 } ? request.InstanceIds.ToHashSet() : null;
        var picked = pickIds is null
            ? selection.Cards.ToList()
            : selection.Cards.Where(c => pickIds.Contains(c.InstanceId)).ToList();

        if (picked.Count == 0)
        {
            return TypedResults.BadRequest("No selection entries match the requested instance IDs.");
        }

        var now = DateTimeOffset.UtcNow;
        foreach (var entry in picked)
        {
            collection.Cards.Add(new CardInstance
            {
                InstanceId = entry.InstanceId,
                PrintingId = entry.PrintingId,
                Foil = entry.Foil,
                Language = entry.Language,
                Condition = entry.Condition,
                AcquiredAt = now,
            });
        }

        var pickedIds = picked.Select(p => p.InstanceId).ToHashSet();
        selection.Cards.RemoveAll(c => pickedIds.Contains(c.InstanceId));
        collection.UpdatedAt = now;

        this.session.Store(collection);
        this.session.Store(selection);
        await this.session.SaveChangesAsync(ct);

        return TypedResults.Ok(new CommitSelectionResponse
        {
            CollectionId = collection.Id,
            CollectionName = collection.Name,
            AddedCount = picked.Count,
            RemainingCount = selection.Cards.Count,
        });
    }

    private async Task<SelectionDocument?> LoadOwnedAsync(Guid id, string ownerSub, CancellationToken ct)
    {
        var doc = await this.session.LoadAsync<SelectionDocument>(id, ct);
        if (doc is null || doc.OwnerSub != ownerSub || doc.ExpiresAt < DateTimeOffset.UtcNow)
        {
            return null;
        }

        return doc;
    }

    private async Task<SelectionResponse> MapAsync(SelectionDocument doc, CancellationToken ct)
    {
        var cards = await this.hydrator.HydrateSelectionAsync(doc.Cards, ct);
        return new SelectionResponse
        {
            Id = doc.Id,
            Cards = cards,
            CreatedAt = doc.CreatedAt,
            ExpiresAt = doc.ExpiresAt,
        };
    }
}
