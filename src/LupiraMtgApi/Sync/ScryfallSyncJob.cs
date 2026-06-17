using Cronos;
using Microsoft.Extensions.Options;

namespace LupiraMtgApi.Sync;

public sealed class ScryfallSyncJob : BackgroundService
{
    private readonly ScryfallSyncRunner _runner;
    private readonly ScryfallSyncOptions _options;
    private readonly ILogger<ScryfallSyncJob> _logger;
    private readonly CronExpression _cron;

    public ScryfallSyncJob(
        ScryfallSyncRunner runner,
        IOptions<ScryfallSyncOptions> options,
        ILogger<ScryfallSyncJob> logger)
    {
        _runner = runner;
        _options = options.Value;
        _logger = logger;
        _cron = CronExpression.Parse(_options.CronSchedule, CronFormat.Standard);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (_options.RunOnStartup)
        {
            _logger.LogInformation("Running Scryfall sync on startup");
            await _runner.RunAsync(stoppingToken);
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            var next = _cron.GetNextOccurrence(DateTimeOffset.UtcNow, TimeZoneInfo.Utc);
            if (next is null)
            {
                _logger.LogWarning("Cron schedule {Schedule} has no future occurrences; sync job exiting", _options.CronSchedule);
                return;
            }

            var delay = next.Value - DateTimeOffset.UtcNow;
            _logger.LogInformation("Next Scryfall sync at {NextRun} (in {Delay})", next.Value, delay);

            try
            {
                await Task.Delay(delay, stoppingToken);
            }
            catch (TaskCanceledException)
            {
                return;
            }

            await _runner.RunAsync(stoppingToken);
        }
    }
}
