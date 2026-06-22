using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.IdentityModel.Tokens;

namespace LupiraMtgApi.IntegrationTests;

/// <summary>
/// Mints HS256-signed JWTs the test host accepts in place of real Authentik tokens (the host's
/// JwtBearer is reconfigured in <see cref="MtgApiTestFactory"/> to trust <see cref="SigningKey"/>
/// with this issuer/audience). Lets the suite exercise the authenticated path without network.
/// </summary>
internal static class TestTokens
{
    public const string Issuer = "https://auth.test/application/o/lupira-mtg/";
    public const string Audience = "lupira-mtg";

    public static readonly SymmetricSecurityKey SigningKey =
        new(Encoding.UTF8.GetBytes("lupira-mtg-integration-test-signing-key-0123456789"));

    public static string Create(string subject, params string[] groups)
    {
        var claims = new List<Claim> { new("sub", subject) };
        claims.AddRange(groups.Select(g => new Claim("groups", g)));

        var token = new JwtSecurityToken(
            issuer: Issuer,
            audience: Audience,
            claims: claims,
            expires: DateTime.UtcNow.AddMinutes(5),
            signingCredentials: new SigningCredentials(SigningKey, SecurityAlgorithms.HmacSha256));

        return new JwtSecurityTokenHandler().WriteToken(token);
    }
}
