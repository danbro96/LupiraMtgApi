namespace LupiraMtgApi.Services.Recognition.Pipeline;

/// <summary>
/// One stage of the scan pipeline. Each step reads/writes a slice of the
/// <see cref="ScanContext"/>. Implementations should be small (single concern), own
/// their own OpenTelemetry activity + metric instruments, and treat the input
/// context as read-only — return a new context via the C# `with` expression rather
/// than mutating in place.
///
/// Adding a new signal = write one IScanStep, register it in the pipeline composition
/// in Program.cs. No surgery in ScanHandler.
/// </summary>
public interface IScanStep
{
    /// <summary>Stable name for tracing and metrics labels. Use dot-namespaced lowercase, e.g. <c>"crop.preprocess"</c>.</summary>
    string Name { get; }

    Task<ScanContext> ExecuteAsync(ScanContext ctx, CancellationToken ct);
}
