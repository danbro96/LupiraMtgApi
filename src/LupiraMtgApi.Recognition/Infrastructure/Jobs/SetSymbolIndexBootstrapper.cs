using LupiraMtgApi.Recognition.Infrastructure.SetSymbol;

namespace LupiraMtgApi.Recognition.Infrastructure.Jobs;

public sealed class SetSymbolIndexBootstrapper : IHostedService
{
    private readonly SetSymbolIndex _index;
    private readonly ILogger<SetSymbolIndexBootstrapper> _logger;

    public SetSymbolIndexBootstrapper(SetSymbolIndex index, ILogger<SetSymbolIndexBootstrapper> logger)
    {
        _index = index;
        _logger = logger;
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        // Build in the background; the set-symbol index is small (<1k entries) so this
        // is fast, but we keep the same fire-and-forget pattern as PHashIndexBootstrapper
        // so a slow first DB connection doesn't block startup.
        _ = Task.Run(
            async () =>
            {
                try
                {
                    await _index.RebuildAsync(cancellationToken);
                }
                catch (OperationCanceledException)
                {
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "SetSymbolIndex initial build failed; set-symbol detection disabled until next sync");
                }
            },
            cancellationToken);

        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
