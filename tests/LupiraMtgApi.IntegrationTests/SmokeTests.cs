using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using LupiraMtgApi.Dtos.Auth;
using Xunit;

namespace LupiraMtgApi.IntegrationTests;

/// <summary>Boot smoke: the app starts against a real Postgres (EF migrations + Marten schema applied on boot),
/// serves health + OpenAPI anonymously, and gates protected routes behind Authentik OIDC bearer auth.</summary>
[Collection("integration")]
public sealed class SmokeTests(MtgApiTestFactory factory)
{
    [Fact]
    public async Task Livez_is_ok()
    {
        var resp = await factory.CreateClient().GetAsync("/livez");
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
    }

    [Fact]
    public async Task Readyz_reports_postgres_reachable()
    {
        var resp = await factory.CreateClient().GetAsync("/readyz");
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
    }

    [Fact]
    public async Task OpenApi_document_is_served()
    {
        var resp = await factory.CreateClient().GetAsync("/openapi/v1.json");
        resp.EnsureSuccessStatusCode();
    }

    [Fact]
    public async Task Whoami_without_a_token_is_unauthorized()
    {
        var resp = await factory.CreateClient().GetAsync("/me");
        Assert.Equal(HttpStatusCode.Unauthorized, resp.StatusCode);
    }

    [Fact]
    public async Task Whoami_with_a_valid_token_projects_subject_and_admin()
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", TestTokens.Create("daniel@example.com", "mtg-admins"));

        var resp = await client.GetAsync("/me");
        resp.EnsureSuccessStatusCode();

        var body = await resp.Content.ReadFromJsonAsync<WhoAmIResponse>();
        Assert.NotNull(body);
        Assert.Equal("daniel@example.com", body!.Subject);
        Assert.True(body.IsAdmin);
    }
}
