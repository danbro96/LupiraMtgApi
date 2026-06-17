namespace LupiraMtgApi.Recognition.Application.Pipeline;

/// <summary>
/// Sequential executor of <see cref="IScanStep"/> instances. Each step receives the
/// context produced by the previous step. Steps are responsible for their own
/// telemetry, error handling, and conditional behavior (e.g., RotationRetryStep
/// is a no-op when the input doesn't warrant retry).
///
/// Pipeline composition is owned by Program.cs DI registration; the executor itself
/// is intentionally trivial — that's the point.
/// </summary>
public sealed class ScanPipeline
{
    private readonly IReadOnlyList<IScanStep> _steps;

    public ScanPipeline(IEnumerable<IScanStep> steps)
    {
        _steps = steps.ToList();
    }

    public async Task<ScanContext> ExecuteAsync(ScanContext context, CancellationToken ct)
    {
        var ctx = context;
        foreach (var step in _steps)
        {
            ct.ThrowIfCancellationRequested();
            ctx = await step.ExecuteAsync(ctx, ct);
        }

        return ctx;
    }
}
