using LupiraMtgApi.Catalog.Data;
using LupiraMtgApi.Catalog.Domain;
using LupiraMtgApi.Catalog.Dtos.Admin;
using Microsoft.EntityFrameworkCore;

namespace LupiraMtgApi.Catalog.Application;

public sealed class SetTypeWeightService
{
    private readonly LupiraMtgDbContext _db;

    public SetTypeWeightService(LupiraMtgDbContext db)
    {
        _db = db;
    }

    public async Task<SetTypeWeightListResponse> ListAsync(CancellationToken ct)
    {
        var weights = await _db.SetTypeWeights
            .AsNoTracking()
            .OrderByDescending(w => w.Weight)
            .ThenBy(w => w.SetType)
            .Select(w => new SetTypeWeightDto
            {
                SetType = w.SetType,
                Weight = w.Weight,
                UpdatedAt = w.UpdatedAt,
            })
            .ToListAsync(ct);

        return new SetTypeWeightListResponse { Weights = weights };
    }

    /// <summary>
    /// Upserts a set-type weight. <paramref name="setType"/> must already be canonicalized
    /// (trimmed, lower-cased, 1..32 chars) and <paramref name="weight"/> validated finite and
    /// non-negative by the caller — input validation is a transport concern owned by the host adapter.
    /// </summary>
    public async Task<SetTypeWeightDto> UpsertAsync(string setType, double weight, CancellationToken ct)
    {
        var now = DateTimeOffset.UtcNow;
        var existing = await _db.SetTypeWeights.FindAsync(new object?[] { setType }, ct);
        if (existing is null)
        {
            existing = new SetTypeWeight
            {
                SetType = setType,
                Weight = weight,
                UpdatedAt = now,
            };
            _db.SetTypeWeights.Add(existing);
        }
        else
        {
            existing.Weight = weight;
            existing.UpdatedAt = now;
        }

        await _db.SaveChangesAsync(ct);

        return new SetTypeWeightDto
        {
            SetType = existing.SetType,
            Weight = existing.Weight,
            UpdatedAt = existing.UpdatedAt,
        };
    }
}
