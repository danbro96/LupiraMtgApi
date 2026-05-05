using LupiraMtgApi.Domain.Collection;
using LupiraMtgApi.Domain.ScanLog;
using LupiraMtgApi.Domain.Selection;
using LupiraMtgApi.Domain.UserProfile;
using Marten;

namespace LupiraMtgApi;

public static class MartenRegistrations
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

        // Per-document schema override: scan logs live in `diagnostics`, not the
        // default `users` Marten schema (set in Program.cs). Keeps engineering-only
        // data out of the user-state namespace.
        opts.Schema.For<ScanLogDocument>()
            .DatabaseSchemaName("diagnostics")
            .Identity(x => x.Id)
            .Index(x => x.OwnerId)
            .Index(x => x.ScannedAt);
    }
}
