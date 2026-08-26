namespace LupiraMtgApi.Recognition.Dtos;

public sealed class ScanListResponse
{
    public required IReadOnlyList<ScanSummaryResponse> Results { get; set; }

    public required int Total { get; set; }
}
