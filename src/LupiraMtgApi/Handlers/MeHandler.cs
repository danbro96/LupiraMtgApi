using LupiraMtgApi.Auth;
using LupiraMtgApi.Data;
using LupiraMtgApi.Data.Entities;
using LupiraMtgApi.Models;
using LupiraMtgApi.Models.Auth;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace LupiraMtgApi.Handlers;

public sealed class MeHandler
{
    private readonly LupiraMtgDbContext _db;

    public MeHandler(LupiraMtgDbContext db)
    {
        _db = db;
    }

    public async Task<Ok<RegisterDeviceResponse>> RegisterAsync(RegisterDeviceRequest? request, CancellationToken ct)
    {
        var (token, hash) = DeviceTokens.Mint();
        var now = DateTimeOffset.UtcNow;
        var device = new DeviceUser
        {
            Id = Guid.NewGuid(),
            TokenHash = hash,
            DisplayName = string.IsNullOrWhiteSpace(request?.DisplayName) ? null : request.DisplayName.Trim(),
            CreatedAt = now,
            LastSeenAt = now,
        };

        _db.Devices.Add(device);
        await _db.SaveChangesAsync(ct);

        return TypedResults.Ok(new RegisterDeviceResponse
        {
            Id = device.Id,
            Token = token,
            DisplayName = device.DisplayName,
        });
    }

    public async Task<Results<Ok<WhoAmIResponse>, UnauthorizedHttpResult>> WhoAmIAsync(HttpContext httpContext, CancellationToken ct)
    {
        if (!httpContext.TryGetOwnerId(out var ownerId))
        {
            return TypedResults.Unauthorized();
        }

        var device = await _db.Devices.AsNoTracking().FirstOrDefaultAsync(d => d.Id == ownerId, ct);
        if (device is null)
        {
            return TypedResults.Unauthorized();
        }

        return TypedResults.Ok(new WhoAmIResponse
        {
            Id = device.Id,
            DisplayName = device.DisplayName,
            CreatedAt = device.CreatedAt,
            LastSeenAt = device.LastSeenAt,
        });
    }
}
