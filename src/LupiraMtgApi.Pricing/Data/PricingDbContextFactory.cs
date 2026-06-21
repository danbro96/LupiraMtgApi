using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace LupiraMtgApi.Pricing.Data;

/// <summary>
/// Design-time factory for `dotnet ef migrations` against the Pricing context. Mirrors the Catalog
/// factory; the host owns the runtime registration. Migrations history lives in the <c>prices</c>
/// schema so the chain stays separate from Catalog's <c>cards.__EFMigrationsHistory</c>.
/// </summary>
public sealed class PricingDbContextFactory : IDesignTimeDbContextFactory<PricingDbContext>
{
    public PricingDbContext CreateDbContext(string[] args)
    {
        var connectionString = Environment.GetEnvironmentVariable("ConnectionStrings__Postgres")
            ?? "Host=localhost;Database=lupira_mtg;Username=lupira_mtg_user;Password=designtime";

        var options = new DbContextOptionsBuilder<PricingDbContext>()
            .UseNpgsql(connectionString, npgsql => npgsql.MigrationsHistoryTable(
                "__EFMigrationsHistory",
                PricingDbContext.Schema))
            .Options;

        return new PricingDbContext(options);
    }
}
