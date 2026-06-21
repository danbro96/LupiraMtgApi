using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Testcontainers.PostgreSql;

namespace LupiraMtgApi.IntegrationTests;

/// <summary>
/// Hosts the real app against an ephemeral Postgres (Testcontainers). Runs in <c>Development</c> so EF migrations
/// auto-apply on boot and Marten auto-creates its schema. MinIO + FlorenceApi get dummy config — the smoke tests
/// exercise only boot, health, OpenAPI, and the auth gate, none of which reach those upstreams.
///
/// Config is injected via environment variables: <c>Program</c> reads <c>ConnectionStrings:Postgres</c> during
/// builder setup (before <c>app.Build()</c>), so an env var — the highest-precedence default source — is the
/// reliable override; <c>ConfigureAppConfiguration</c> would lose to the committed appsettings value.
/// </summary>
public sealed class MtgApiTestFactory : WebApplicationFactory<Program>
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:17-alpine").Build();

    public MtgApiTestFactory()
    {
        _postgres.StartAsync().GetAwaiter().GetResult();
        Environment.SetEnvironmentVariable("ConnectionStrings__Postgres", _postgres.GetConnectionString());
        Environment.SetEnvironmentVariable("Minio__Endpoint", "localhost:9000");
        Environment.SetEnvironmentVariable("Minio__AccessKey", "test");
        Environment.SetEnvironmentVariable("Minio__SecretKey", "testsecret");
        Environment.SetEnvironmentVariable("Minio__Bucket", "test");
        Environment.SetEnvironmentVariable("Florence__Url", "http://localhost:9");
        Environment.SetEnvironmentVariable("Florence__ApiKey", "test");
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder) => builder.UseEnvironment("Development");

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (disposing) _postgres.DisposeAsync().AsTask().GetAwaiter().GetResult();
    }
}
