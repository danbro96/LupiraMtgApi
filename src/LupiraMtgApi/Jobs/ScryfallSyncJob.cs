using Cronos;
using Microsoft.Extensions.Options;

namespace LupiraMtgApi.Jobs;

public sealed class ScryfallSyncJob : BackgroundService
{
    private readonly ScryfallSyncRunner runner;
    private readonly ScryfallSyncOptions options;
    private readonly ILogger<ScryfallSyncJob> logger;
    private readonly CronExpression cron;

    public ScryfallSyncJob(
        ScryfallSyncRunner runner,
        IOptions<ScryfallSyncOptions> options,
        ILogger<ScryfallSyncJob> logger)
    {
        this.runner = runner;
        this.options = options.Value;
        this.logger = logger;
        this.cron = CronExpression.Parse(this.options.CronSchedule, CronFormat.Standard);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (this.options.RunOnStartup)
        {
            this.logger.LogInformation("Running Scryfall sync on startup");
            await this.runner.RunAsync(stoppingToken);
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            var next = this.cron.GetNextOccurrence(DateTimeOffset.UtcNow, TimeZoneInfo.Utc);
            if (next is null)
            {
                this.logger.LogWarning("Cron schedule {Schedule} has no future occurrences; sync job exiting", this.options.CronSchedule);
                return;
            }

            var delay = next.Value - DateTimeOffset.UtcNow;
            this.logger.LogInformation("Next Scryfall sync at {NextRun} (in {Delay})", next.Value, delay);

            try
            {
                await Task.Delay(delay, stoppingToken);
            }
            catch (TaskCanceledException)
            {
                return;
            }

            await this.runner.RunAsync(stoppingToken);
        }
    }
}
