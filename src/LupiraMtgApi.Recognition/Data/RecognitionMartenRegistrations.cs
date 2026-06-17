using Marten;

namespace LupiraMtgApi.Recognition.Data;

/// <summary>
/// Marten document configuration owned by the Recognition context. Scan logs live in the
/// <c>diagnostics</c> schema (engineering-only data) rather than the default <c>users</c> schema set
/// by the host. The host composes this alongside the other contexts' registrations in its
/// <c>AddMarten</c> call.
/// </summary>
public static class RecognitionMartenRegistrations
{
    public static void Configure(StoreOptions opts)
    {
        opts.Schema.For<ScanLogDocument>()
            .DatabaseSchemaName("diagnostics")
            .Identity(x => x.Id)
            .Index(x => x.OwnerId)
            .Index(x => x.ScannedAt)
            .Index(x => x.FeedbackCorrectPrintingId);
    }
}
