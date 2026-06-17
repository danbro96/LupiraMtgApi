using LupiraMtgApi.Collections.Application;
using LupiraMtgApi.Collections.Mappers;

namespace Microsoft.Extensions.DependencyInjection;

/// <summary>
/// Registers the Collections bounded context — the card-instance hydrator and the collection,
/// selection, and my-cards Application services. Depends only on the DI abstractions, not ASP.NET.
/// </summary>
public static class CollectionsServiceCollectionExtensions
{
    public static IServiceCollection AddCollections(this IServiceCollection services)
    {
        services.AddScoped<CardInstanceHydrator>();
        services.AddScoped<CollectionsService>();
        services.AddScoped<SelectionsService>();
        services.AddScoped<MyCardsService>();
        return services;
    }
}
