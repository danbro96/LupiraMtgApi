using LupiraMtgApi.Catalog.Data;
using LupiraMtgApi.Catalog.Domain;
using LupiraMtgApi.Catalog.Mappers;
using LupiraMtgApi.Collections.Domain;
using LupiraMtgApi.Collections.Dtos;
using LupiraMtgApi.Pricing.Application;
using Microsoft.EntityFrameworkCore;

namespace LupiraMtgApi.Collections.Mappers;

/// <summary>
/// Hydrates lists of <see cref="CardInstance"/> / <see cref="SelectionEntry"/> into
/// API response shapes by batch-fetching the referenced <see cref="CardPrinting"/> rows
/// from EF Core and minting presigned image URLs in parallel.
/// </summary>
public sealed class CardInstanceHydrator
{
    private readonly LupiraMtgDbContext _db;
    private readonly CardPrintingMapper _mapper;
    private readonly CardPriceLookup _prices;

    public CardInstanceHydrator(LupiraMtgDbContext db, CardPrintingMapper mapper, CardPriceLookup prices)
    {
        _db = db;
        _mapper = mapper;
        _prices = prices;
    }

    public async Task<List<CardInstanceDto>> HydrateAsync(
        IReadOnlyList<CardInstance> cards,
        Guid? collectionId,
        string? collectionName,
        CancellationToken ct)
    {
        if (cards.Count == 0)
        {
            return new List<CardInstanceDto>();
        }

        var byPrintingId = await this.LoadPrintingsAsync(cards.Select(c => c.PrintingId), ct);
        var setNames = await this.LoadSetNamesAsync(byPrintingId.Values, ct);
        var prices = await _prices.GetAsync(byPrintingId.Keys, ct);

        var result = new List<CardInstanceDto>(cards.Count);
        foreach (var card in cards)
        {
            if (!byPrintingId.TryGetValue(card.PrintingId, out var printing))
            {
                continue;
            }

            var setName = setNames.GetValueOrDefault(printing.SetCode, printing.SetCode);
            var printingResponse = await _mapper.MapAsync(printing, setName, prices.GetValueOrDefault(printing.Id), ct);

            result.Add(new CardInstanceDto
            {
                InstanceId = card.InstanceId,
                Printing = printingResponse,
                IsFoil = card.IsFoil,
                Language = card.Language,
                Condition = card.Condition,
                AcquiredAt = card.AcquiredAt,
                CollectionId = collectionId,
                CollectionName = collectionName,
            });
        }

        return result;
    }

    public async Task<List<SelectionEntryDto>> HydrateSelectionAsync(
        IReadOnlyList<SelectionEntry> entries,
        CancellationToken ct)
    {
        if (entries.Count == 0)
        {
            return new List<SelectionEntryDto>();
        }

        var byPrintingId = await this.LoadPrintingsAsync(entries.Select(e => e.PrintingId), ct);
        var setNames = await this.LoadSetNamesAsync(byPrintingId.Values, ct);
        var prices = await _prices.GetAsync(byPrintingId.Keys, ct);

        var result = new List<SelectionEntryDto>(entries.Count);
        foreach (var entry in entries)
        {
            if (!byPrintingId.TryGetValue(entry.PrintingId, out var printing))
            {
                continue;
            }

            var setName = setNames.GetValueOrDefault(printing.SetCode, printing.SetCode);
            var printingResponse = await _mapper.MapAsync(printing, setName, prices.GetValueOrDefault(printing.Id), ct);

            result.Add(new SelectionEntryDto
            {
                InstanceId = entry.InstanceId,
                Printing = printingResponse,
                IsFoil = entry.IsFoil,
                Language = entry.Language,
                Condition = entry.Condition,
                Confidence = entry.Confidence,
            });
        }

        return result;
    }

    /// <summary>
    /// Bulk-fetches just the names of the referenced printings. Used by paginated
    /// listings to sort by name *before* slicing — so we hydrate (presign URLs etc.)
    /// only the page we actually return.
    /// </summary>
    public async Task<Dictionary<string, string>> LoadPrintingNamesAsync(
        IEnumerable<string> printingIds,
        CancellationToken ct)
    {
        var distinct = printingIds.Distinct().ToList();
        if (distinct.Count == 0)
        {
            return new Dictionary<string, string>();
        }

        return await _db.CardPrintings
            .AsNoTracking()
            .Where(p => distinct.Contains(p.Id))
            .Select(p => new { p.Id, p.Name })
            .ToDictionaryAsync(x => x.Id, x => x.Name, ct);
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

        var rows = await _db.CardPrintings
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

        return await _db.Sets
            .AsNoTracking()
            .Where(s => setCodes.Contains(s.Code))
            .ToDictionaryAsync(s => s.Code, s => s.Name, ct);
    }
}
