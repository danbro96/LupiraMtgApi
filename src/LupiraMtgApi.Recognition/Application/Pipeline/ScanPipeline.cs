namespace LupiraMtgApi.Recognition.Application.Pipeline;

/// <summary>
/// Sequential executor of <see cref="IScanStep"/> instances; each step receives the context the previous one
/// produced. Steps own their own telemetry, error handling and conditional behaviour (e.g. RotationRetryStep
/// no-ops when the input doesn't warrant a retry), which is why the executor itself stays trivial.
/// Composition lives in Program.cs DI registration.
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
