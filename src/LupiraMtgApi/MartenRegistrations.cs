using LupiraMtgApi.Domain.Collection;
using LupiraMtgApi.Domain.Selection;
using LupiraMtgApi.Domain.UserProfile;
using Marten;

namespace LupiraMtgApi;

public static class MartenRegistrations
{
    public static void Configure(StoreOptions opts)
    {
        opts.Schema.For<UserProfileDocument>()
            .Identity(u => u.Sub);

        opts.Schema.For<SelectionDocument>()
            .Identity(s => s.Id)
            .Index(x => x.OwnerSub);

        opts.Schema.For<CollectionDocument>()
            .Identity(c => c.Id)
            .Index(x => x.OwnerSub);
    }
}
