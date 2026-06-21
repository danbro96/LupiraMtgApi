using LupiraMtgApi.Pricing.Application;

namespace Microsoft.Extensions.DependencyInjection;

/// <summary>
/// Registers the Pricing bounded context — price ingest, latest lookup, and history reads. Like the
/// other contexts, the host owns the environment-specific <c>AddDbContext&lt;PricingDbContext&gt;</c>
/// (connection string + migrations gating); this only registers the Application services + options.
/// </summary>
public static class PricingServiceCollectionExtensions
{
    public static IServiceCollection AddPricing(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<LupiraMtgApi.Pricing.PricingOptions>(configuration.GetSection("Pricing"));

        services.AddScoped<PriceIngestService>();
        services.AddScoped<CardPriceLookup>();
        services.AddScoped<PriceHistoryService>();

        return services;
    }
}
