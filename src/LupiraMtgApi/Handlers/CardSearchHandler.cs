using LupiraMtgApi.Data;
using LupiraMtgApi.Data.Entities;
using LupiraMtgApi.Models;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using LupiraMtgApi.Models.Cards;
namespace LupiraMtgApi.Handlers;

public sealed class CardSearchHandler
{
    private const int DefaultLimit = 25;
    private const int MaxLimit = 100;

    private readonly LupiraMtgDbContext _db;
    private readonly CardPrintingMapper _mapper;

    public CardSearchHandler(LupiraMtgDbContext db, CardPrintingMapper mapper)
    {
        _db = db;
        _mapper = mapper;
    }

    public async Task<Results<Ok<CardPrintingResponse>, NotFound>> GetByIdAsync(string printingId, CancellationToken ct)
    {
        var printing = await _db.CardPrintings
            .AsNoTracking()
            .FirstOrDefaultAsync(p => p.Id == printingId, ct);

        if (printing is null)
        {
            return TypedResults.NotFound();
        }

        var setName = await _db.Sets
            .AsNoTracking()
            .Where(s => s.Code == printing.SetCode)
            .Select(s => s.Name)
            .FirstOrDefaultAsync(ct) ?? printing.SetCode;

        var response = await _mapper.MapAsync(printing, setName, ct);
        return TypedResults.Ok(response);
    }

    public async Task<Ok<CardSearchResponse>> SearchAsync(
        string? q,
        string? set,
        string? color,
        string? rarity,
        int? limit,
        CancellationToken ct)
    {
        var take = Math.Clamp(limit ?? DefaultLimit, 1, MaxLimit);

        IQueryable<CardPrinting> query = _db.CardPrintings.AsNoTracking();

        if (!string.IsNullOrWhiteSpace(set))
        {
            var setCode = set.ToLowerInvariant();
            query = query.Where(p => p.SetCode == setCode);
        }

        if (!string.IsNullOrWhiteSpace(rarity))
        {
            var r = rarity.ToLowerInvariant();
            query = query.Where(p => p.Rarity == r);
        }

        if (!string.IsNullOrWhiteSpace(color))
        {
            var c = color.ToUpperInvariant();
            query = query.Where(p => p.ColorIdentity.Contains(c));
        }

        if (!string.IsNullOrWhiteSpace(q))
        {
            var term = q.Trim();
            query = query
                .Where(p => EF.Functions.TrigramsSimilarity(p.Name, term) > 0.2)
                .OrderByDescending(p => EF.Functions.TrigramsSimilarity(p.Name, term));
        }
        else
        {
            query = query.OrderBy(p => p.Name).ThenBy(p => p.SetCode).ThenBy(p => p.CollectorNumber);
        }

        var total = await query.CountAsync(ct);
        var rows = await query.Take(take).ToListAsync(ct);

        var setCodes = rows.Select(r => r.SetCode).Distinct().ToList();
        var setNames = await _db.Sets
            .AsNoTracking()
            .Where(s => setCodes.Contains(s.Code))
            .ToDictionaryAsync(s => s.Code, s => s.Name, ct);

        var results = new List<CardPrintingResponse>(rows.Count);
        foreach (var row in rows)
        {
            var setName = setNames.GetValueOrDefault(row.SetCode, row.SetCode);
            results.Add(await _mapper.MapAsync(row, setName, ct));
        }

        return TypedResults.Ok(new CardSearchResponse { Results = results, Total = total });
    }
}
