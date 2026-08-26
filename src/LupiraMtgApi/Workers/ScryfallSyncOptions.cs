namespace LupiraMtgApi.Workers;

public sealed class ScryfallSyncOptions
{
    public string CronSchedule { get; set; } = "0 4 * * *";

    public bool RunOnStartup { get; set; }

    public bool DownloadImages { get; set; } = true;

    public bool ComputePHashes { get; set; } = true;

    public int InterRequestDelayMs { get; set; } = 100;

    public int BatchSize { get; set; } = 500;
}
