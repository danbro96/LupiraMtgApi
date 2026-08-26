namespace LupiraMtgApi.Models.Sync;

public sealed class SyncRunResponse
{
    public required string Status { get; set; }

    public required DateTimeOffset StartedAt { get; set; }

    public required DateTimeOffset? FinishedAt { get; set; }

    public required int PrintingsTotal { get; set; }

    public required int PrintingsAdded { get; set; }

    public required int PrintingsUpdated { get; set; }

    public required int ImagesUploaded { get; set; }

    public required int PHashesComputed { get; set; }

    public required string? Error { get; set; }
}
