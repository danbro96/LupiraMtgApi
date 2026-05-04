namespace LupiraMtgApi.Models.Sync;

public sealed class SyncRunResponse
{
    public required string Status { get; set; }

    public required DateTimeOffset StartedAt { get; set; }

    public DateTimeOffset? FinishedAt { get; set; }

    public int PrintingsTotal { get; set; }

    public int PrintingsAdded { get; set; }

    public int PrintingsUpdated { get; set; }

    public int ImagesUploaded { get; set; }

    public int PHashesComputed { get; set; }

    public string? Error { get; set; }
}
