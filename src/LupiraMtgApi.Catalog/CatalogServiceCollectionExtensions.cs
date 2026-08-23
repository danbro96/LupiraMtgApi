using LupiraMtgApi.Catalog.Application;
using LupiraMtgApi.Catalog.Infrastructure.Scryfall;
using LupiraMtgApi.Catalog.Infrastructure.Storage;
using LupiraMtgApi.Catalog.Mappers;

namespace Microsoft.Extensions.DependencyInjection;

/// <summary>
/// Registers the Catalog bounded context — image storage, the Scryfall catalog source, the
/// card/set/weight Application services, and the printing mapper. Lives in Catalog and depends only
/// on the DI abstractions, not ASP.NET, so the "no ASP.NET in a context library" rule holds.
///
/// <para>
/// The host still owns environment-specific composition: <c>AddDbContext&lt;LupiraMtgDbContext&gt;</c>
/// (connection string + dynamic-JSON datasource + migrations gating).
/// </para>
/// </summary>
public static class CatalogServiceCollectionExtensions
{
    public static IServiceCollection AddCatalog(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<S3ImageStoreOptions>(configuration.GetSection("S3"));
        services.AddSingleton<IImageStore, S3ImageStore>();

        services.AddScoped<CardPrintingMapper>();
        services.AddScoped<CardCatalogService>();
        services.AddScoped<SetService>();
        services.AddScoped<SetTypeWeightService>();

        services.AddHttpClient<ICardCatalogSource, ScryfallCatalogSource>(client =>
        {
            client.BaseAddress = new Uri("https://api.scryfall.com/");
            client.DefaultRequestHeaders.UserAgent.ParseAdd("LupiraMtgApi/0.1 (+https://github.com/danbro96/LupiraMtgApi)");
            client.DefaultRequestHeaders.Accept.ParseAdd("application/json");
            client.Timeout = TimeSpan.FromMinutes(5);
        });

        return services;
    }
}
