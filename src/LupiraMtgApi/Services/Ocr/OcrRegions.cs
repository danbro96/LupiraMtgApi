namespace LupiraMtgApi.Services.Ocr;

public sealed class OcrRegions
{
    public required IReadOnlyList<OcrRegion> Regions { get; set; }

    public static OcrRegions Empty { get; } = new() { Regions = Array.Empty<OcrRegion>() };
}
