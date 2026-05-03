using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace LupiraMtgApi.Data;

/// <summary>
/// Design-time factory used by `dotnet ef migrations` so the tool can build the model
/// without spinning up the full application host (which would also try to bootstrap
/// Marten, HttpClient, MinIO, etc. — none of which are needed to scaffold a migration).
/// </summary>
public sealed class LupiraMtgDbContextFactory : IDesignTimeDbContextFactory<LupiraMtgDbContext>
{
    public LupiraMtgDbContext CreateDbContext(string[] args)
    {
        var connectionString = Environment.GetEnvironmentVariable("ConnectionStrings__Postgres")
            ?? "Host=localhost;Database=lupira_mtg;Username=lupira_mtg_user;Password=designtime";

        var options = new DbContextOptionsBuilder<LupiraMtgDbContext>()
            .UseNpgsql(connectionString, npgsql => npgsql.MigrationsHistoryTable(
                "__EFMigrationsHistory",
                LupiraMtgDbContext.Schema))
            .Options;

        return new LupiraMtgDbContext(options);
    }
}
