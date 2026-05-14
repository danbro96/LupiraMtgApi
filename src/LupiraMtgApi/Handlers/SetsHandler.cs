using LupiraMtgApi.Data;
using LupiraMtgApi.Data.Entities;
using LupiraMtgApi.Models.Sets;
using LupiraMtgApi.Services.Storage;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace LupiraMtgApi.Handlers;

public sealed class SetsHandler
{
    private const int DefaultLimit = 50;
    private const int MaxLimit = 200;
    private static readonly TimeSpan IconPresignExpiry = TimeSpan.FromMinutes(15);

    private readonly LupiraMtgDbContext _db;
    private readonly IImageStore _images;

    public SetsHandler(LupiraMtgDbContext db, IImageStore images)
    {
        _db = db;
        _images = images;
    }

    public async Task<Ok<SetListResponse>> ListAsync(
        string? setType,
        string? sort,
        string? order,
        int? take,
        int? skip,
        CancellationToken ct)
    {
        var clampedTake = Math.Clamp(take ?? DefaultLimit, 1, MaxLimit);
        var clampedSkip = Math.Max(skip ?? 0, 0);

        IQueryable<ScryfallSet> query = _db.Sets.AsNoTracking();

        if (!string.IsNullOrWhiteSpace(setType))
        {
            var st = setType.ToLowerInvariant();
            query = query.Where(s => s.SetType == st);
        }

        var ascending = !string.Equals(order, "desc", StringComparison.OrdinalIgnoreCase);
        query = (sort?.ToLowerInvariant()) switch
        {
            "code" => ascending ? query.OrderBy(s => s.Code) : query.OrderByDescending(s => s.Code),
            "name" => ascending ? query.OrderBy(s => s.Name) : query.OrderByDescending(s => s.Name),
            _ => ascending
                ? query.OrderBy(s => s.ReleasedAt).ThenBy(s => s.Code)
                : query.OrderByDescending(s => s.ReleasedAt).ThenByDescending(s => s.Code),
        };

        var total = await query.CountAsync(ct);
        var rows = await query.Skip(clampedSkip).Take(clampedTake).ToListAsync(ct);

        var results = new List<SetResponse>(rows.Count);
        foreach (var row in rows)
        {
            results.Add(await MapAsync(row, ct));
        }

        return TypedResults.Ok(new SetListResponse { Results = results, Total = total });
    }

    public async Task<Results<Ok<SetResponse>, NotFound>> GetByCodeAsync(string code, CancellationToken ct)
    {
        var canonical = code.ToLowerInvariant();
        var set = await _db.Sets.AsNoTracking().FirstOrDefaultAsync(s => s.Code == canonical, ct);
        if (set is null)
        {
            return TypedResults.NotFound();
        }

        return TypedResults.Ok(await MapAsync(set, ct));
    }

    private async Task<SetResponse> MapAsync(ScryfallSet set, CancellationToken ct)
    {
        string? iconUrl = null;
        if (set.IconObjectKey is { Length: > 0 })
        {
            iconUrl = await _images.CreatePresignedGetUrlAsync(set.IconObjectKey, IconPresignExpiry, ct);
        }

        return new SetResponse
        {
            Code = set.Code,
            Name = set.Name,
            SetType = set.SetType,
            ReleasedAt = set.ReleasedAt,
            CardCount = set.CardCount,
            IconUrl = iconUrl,
        };
    }
}
