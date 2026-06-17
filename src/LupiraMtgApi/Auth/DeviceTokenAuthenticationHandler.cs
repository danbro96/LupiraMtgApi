using LupiraMtgApi.Catalog.Data;
using Microsoft.AspNetCore.Authentication;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using System.Security.Claims;
using System.Text.Encodings.Web;

namespace LupiraMtgApi.Auth;

public sealed class DeviceTokenAuthenticationHandler : AuthenticationHandler<DeviceTokenAuthOptions>
{
    private const string BearerPrefix = "Bearer ";

    private readonly LupiraMtgDbContext _db;

    public DeviceTokenAuthenticationHandler(
        IOptionsMonitor<DeviceTokenAuthOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder,
        LupiraMtgDbContext db)
        : base(options, logger, encoder)
    {
        _db = db;
    }

    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!this.Request.Headers.TryGetValue(DeviceTokenAuthOptions.HeaderName, out var raw) || raw.Count == 0)
        {
            return AuthenticateResult.NoResult();
        }

        var header = raw[0];
        if (header is null || !header.StartsWith(BearerPrefix, StringComparison.Ordinal))
        {
            return AuthenticateResult.NoResult();
        }

        var token = header[BearerPrefix.Length..].Trim();
        if (token.Length == 0 || !token.StartsWith(DeviceTokens.Prefix, StringComparison.Ordinal))
        {
            return AuthenticateResult.Fail("Token format not recognized.");
        }

        var hash = DeviceTokens.HashToken(token);
        var device = await _db.Devices.FirstOrDefaultAsync(d => d.TokenHash == hash, this.Context.RequestAborted);
        if (device is null)
        {
            return AuthenticateResult.Fail("Unknown device token.");
        }

        var now = DateTimeOffset.UtcNow;
        if (this.Options.LastSeenWriteInterval == TimeSpan.Zero ||
            now - device.LastSeenAt > this.Options.LastSeenWriteInterval)
        {
            device.LastSeenAt = now;
            try
            {
                await _db.SaveChangesAsync(this.Context.RequestAborted);
            }
            catch (DbUpdateConcurrencyException)
            {
                // Another concurrent request updated LastSeenAt — fine, drop our update.
            }
        }

        var claims = new List<Claim>
        {
            new("sub", device.Id.ToString()),
        };
        if (!string.IsNullOrEmpty(device.DisplayName))
        {
            claims.Add(new Claim("name", device.DisplayName));
        }

        var identity = new ClaimsIdentity(claims, DeviceTokenAuthOptions.SchemeName, "name", null);
        var principal = new ClaimsPrincipal(identity);
        var ticket = new AuthenticationTicket(principal, DeviceTokenAuthOptions.SchemeName);
        return AuthenticateResult.Success(ticket);
    }
}
