using LupiraMtgApi.Services;

namespace LupiraMtgApi.Jobs;

public sealed class PHashIndexBootstrapper : IHostedService
{
    private readonly PHashIndex index;
    private readonly ILogger<PHashIndexBootstrapper> logger;

    public PHashIndexBootstrapper(PHashIndex index, ILogger<PHashIndexBootstrapper> logger)
    {
        this.index = index;
        this.logger = logger;
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        // Build in the background — startup should not wait on a 600k-row scan.
        // Until the index is loaded, ScanHandler degrades to OCR-only candidates
        // (PHashIndex.IsLoaded is false → empty pHash hits, OCR branch carries the recognition).
        _ = Task.Run(async () =>
        {
            try
            {
                await this.index.RebuildAsync(cancellationToken);
            }
            catch (OperationCanceledException)
            {
                // Process shutting down before the rebuild finished.
            }
            catch (Exception ex)
            {
                this.logger.LogError(ex, "PHashIndex initial build failed; recognition will be OCR-only until the next sync");
            }
        }, cancellationToken);

        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
