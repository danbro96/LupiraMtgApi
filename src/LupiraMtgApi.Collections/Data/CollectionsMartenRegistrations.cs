using LupiraMtgApi.Collections.Domain;
using Marten;

namespace LupiraMtgApi.Collections.Data;

/// <summary>
/// Marten document configuration owned by the Collections context — user profiles, collections, and
/// selections in the default <c>users</c> schema (set by the host). The host composes this alongside
/// the other contexts' registrations in its <c>AddMarten</c> call.
/// </summary>
public static class CollectionsMartenRegistrations
{
    public static void Configure(StoreOptions opts)
    {
        opts.Schema.For<UserProfileDocument>()
            .Identity(u => u.Id);

        opts.Schema.For<SelectionDocument>()
            .Identity(s => s.Id)
            .Index(x => x.OwnerId);

        opts.Schema.For<CollectionDocument>()
            .Identity(c => c.Id)
            .Index(x => x.OwnerId);
    }
}
