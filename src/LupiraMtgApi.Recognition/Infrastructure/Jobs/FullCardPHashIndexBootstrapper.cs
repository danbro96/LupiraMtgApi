using LupiraMtgApi.Recognition.Infrastructure.Imaging;

namespace LupiraMtgApi.Recognition.Infrastructure.Jobs;

public sealed class FullCardPHashIndexBootstrapper : IHostedService
{
    private readonly FullCardPHashIndex _index;
    private readonly ILogger<FullCardPHashIndexBootstrapper> _logger;

    public FullCardPHashIndexBootstrapper(FullCardPHashIndex index, ILogger<FullCardPHashIndexBootstrapper> logger)
    {
        _index = index;
        _logger = logger;
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        // Build in the background — startup must not wait on the same ~80K-row scan
        // PHashIndex already does. Until both indexes load, the scan path degrades to
        // OCR-only candidates, same behavior as the existing single-index startup.
        _ = Task.Run(
            async () =>
            {
                try
                {
                    await _index.RebuildAsync(cancellationToken);
                }
                catch (OperationCanceledException)
                {
                    // Process shutting down before the rebuild finished.
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "FullCardPHashIndex initial build failed; full-card pHash signal disabled until next sync");
                }
            },
            cancellationToken);

        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
