using LupiraMtgApi.Auth;
using LupiraMtgApi.Data;
using LupiraMtgApi.Data.Entities;
using LupiraMtgApi.Models;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using LupiraMtgApi.Models.Auth;
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
            Sub = Guid.NewGuid(),
            TokenHash = hash,
            DisplayName = string.IsNullOrWhiteSpace(request?.DisplayName) ? null : request.DisplayName.Trim(),
            CreatedAt = now,
            LastSeenAt = now,
        };

        _db.Devices.Add(device);
        await _db.SaveChangesAsync(ct);

        return TypedResults.Ok(new RegisterDeviceResponse
        {
            Sub = device.Sub,
            Token = token,
            DisplayName = device.DisplayName,
        });
    }

    public async Task<Results<Ok<WhoAmIResponse>, UnauthorizedHttpResult>> WhoAmIAsync(HttpContext httpContext, CancellationToken ct)
    {
        var subClaim = httpContext.User.FindFirst("sub")?.Value;
        if (!Guid.TryParse(subClaim, out var sub))
        {
            return TypedResults.Unauthorized();
        }

        var device = await _db.Devices.AsNoTracking().FirstOrDefaultAsync(d => d.Sub == sub, ct);
        if (device is null)
        {
            return TypedResults.Unauthorized();
        }

        return TypedResults.Ok(new WhoAmIResponse
        {
            Sub = device.Sub,
            DisplayName = device.DisplayName,
            CreatedAt = device.CreatedAt,
            LastSeenAt = device.LastSeenAt,
        });
    }
}
