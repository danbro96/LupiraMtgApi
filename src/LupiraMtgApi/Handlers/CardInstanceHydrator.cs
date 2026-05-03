using LupiraMtgApi.Data;
using LupiraMtgApi.Data.Entities;
using LupiraMtgApi.Domain.Collection;
using LupiraMtgApi.Domain.Selection;
using LupiraMtgApi.Models;
using LupiraMtgApi.Models.Collections;
using LupiraMtgApi.Models.Selections;
using Microsoft.EntityFrameworkCore;

namespace LupiraMtgApi.Handlers;

/// <summary>
/// Hydrates lists of <see cref="CardInstance"/> / <see cref="SelectionEntry"/> into
/// API response shapes by batch-fetching the referenced <see cref="CardPrinting"/> rows
/// from EF Core and minting presigned image URLs in parallel.
/// </summary>
public sealed class CardInstanceHydrator
{
    private readonly LupiraMtgDbContext db;
    private readonly CardPrintingMapper mapper;

    public CardInstanceHydrator(LupiraMtgDbContext db, CardPrintingMapper mapper)
    {
        this.db = db;
        this.mapper = mapper;
    }

    public async Task<List<CardInstanceResponse>> HydrateAsync(
        IReadOnlyList<CardInstance> cards,
        Guid? collectionId,
        string? collectionName,
        CancellationToken ct)
    {
        if (cards.Count == 0)
        {
            return new List<CardInstanceResponse>();
        }

        var byPrintingId = await this.LoadPrintingsAsync(cards.Select(c => c.PrintingId), ct);
        var setNames = await this.LoadSetNamesAsync(byPrintingId.Values, ct);

        var result = new List<CardInstanceResponse>(cards.Count);
        foreach (var card in cards)
        {
            if (!byPrintingId.TryGetValue(card.PrintingId, out var printing))
            {
                continue;
            }

            var setName = setNames.GetValueOrDefault(printing.SetCode, printing.SetCode);
            var printingResponse = await this.mapper.MapAsync(printing, setName, ct);

            result.Add(new CardInstanceResponse
            {
                InstanceId = card.InstanceId,
                Printing = printingResponse,
                Foil = card.Foil,
                Language = card.Language,
                Condition = card.Condition,
                AcquiredAt = card.AcquiredAt,
                CollectionId = collectionId,
                CollectionName = collectionName,
            });
        }

        return result;
    }

    public async Task<List<SelectionEntryResponse>> HydrateSelectionAsync(
        IReadOnlyList<SelectionEntry> entries,
        CancellationToken ct)
    {
        if (entries.Count == 0)
        {
            return new List<SelectionEntryResponse>();
        }

        var byPrintingId = await this.LoadPrintingsAsync(entries.Select(e => e.PrintingId), ct);
        var setNames = await this.LoadSetNamesAsync(byPrintingId.Values, ct);

        var result = new List<SelectionEntryResponse>(entries.Count);
        foreach (var entry in entries)
        {
            if (!byPrintingId.TryGetValue(entry.PrintingId, out var printing))
            {
                continue;
            }

            var setName = setNames.GetValueOrDefault(printing.SetCode, printing.SetCode);
            var printingResponse = await this.mapper.MapAsync(printing, setName, ct);

            result.Add(new SelectionEntryResponse
            {
                InstanceId = entry.InstanceId,
                Printing = printingResponse,
                Foil = entry.Foil,
                Language = entry.Language,
                Condition = entry.Condition,
                Confidence = entry.Confidence,
            });
        }

        return result;
    }

    private async Task<Dictionary<string, CardPrinting>> LoadPrintingsAsync(
        IEnumerable<string> ids,
        CancellationToken ct)
    {
        var distinct = ids.Distinct().ToList();
        if (distinct.Count == 0)
        {
            return new Dictionary<string, CardPrinting>();
        }

        var rows = await this.db.CardPrintings
            .AsNoTracking()
            .Where(p => distinct.Contains(p.Id))
            .ToListAsync(ct);

        return rows.ToDictionary(p => p.Id);
    }

    private async Task<Dictionary<string, string>> LoadSetNamesAsync(
        IEnumerable<CardPrinting> printings,
        CancellationToken ct)
    {
        var setCodes = printings.Select(p => p.SetCode).Distinct().ToList();
        if (setCodes.Count == 0)
        {
            return new Dictionary<string, string>();
        }

        return await this.db.Sets
            .AsNoTracking()
            .Where(s => setCodes.Contains(s.Code))
            .ToDictionaryAsync(s => s.Code, s => s.Name, ct);
    }
}
