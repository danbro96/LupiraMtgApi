using LupiraMtgApi.Data;
using LupiraMtgApi.Data.Entities;
using LupiraMtgApi.Models.Admin;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace LupiraMtgApi.Handlers;

public sealed class SetTypeWeightHandler
{
    private readonly LupiraMtgDbContext _db;

    public SetTypeWeightHandler(LupiraMtgDbContext db)
    {
        _db = db;
    }

    public async Task<Ok<SetTypeWeightListResponse>> ListAsync(CancellationToken ct)
    {
        var weights = await _db.SetTypeWeights
            .AsNoTracking()
            .OrderByDescending(w => w.Weight)
            .ThenBy(w => w.SetType)
            .Select(w => new SetTypeWeightResponse
            {
                SetType = w.SetType,
                Weight = w.Weight,
                UpdatedAt = w.UpdatedAt,
            })
            .ToListAsync(ct);

        return TypedResults.Ok(new SetTypeWeightListResponse { Weights = weights });
    }

    public async Task<Results<Ok<SetTypeWeightResponse>, ProblemHttpResult>> UpsertAsync(
        string setType,
        UpdateSetTypeWeightRequest request,
        CancellationToken ct)
    {
        var canonical = setType.Trim().ToLowerInvariant();
        if (canonical.Length == 0 || canonical.Length > 32)
        {
            return Problems.BadRequest("setType must be 1..32 characters.");
        }
        if (!double.IsFinite(request.Weight) || request.Weight < 0)
        {
            return Problems.BadRequest("weight must be a finite non-negative number.");
        }

        var now = DateTimeOffset.UtcNow;
        var existing = await _db.SetTypeWeights.FindAsync(new object?[] { canonical }, ct);
        if (existing is null)
        {
            existing = new SetTypeWeight
            {
                SetType = canonical,
                Weight = request.Weight,
                UpdatedAt = now,
            };
            _db.SetTypeWeights.Add(existing);
        }
        else
        {
            existing.Weight = request.Weight;
            existing.UpdatedAt = now;
        }

        await _db.SaveChangesAsync(ct);

        return TypedResults.Ok(new SetTypeWeightResponse
        {
            SetType = existing.SetType,
            Weight = existing.Weight,
            UpdatedAt = existing.UpdatedAt,
        });
    }
}
