namespace LupiraMtgApi.Recognition.Domain;

public sealed class OcrRegions
{
    public required IReadOnlyList<OcrRegion> Regions { get; set; }

    /// <summary>Width (px) of the image Florence actually OCR'd. 0 when unknown — caller should fall back to its own dims.</summary>
    public int ImageWidth { get; set; }

    /// <summary>Height (px) of the image Florence actually OCR'd. 0 when unknown.</summary>
    public int ImageHeight { get; set; }

    public static OcrRegions Empty { get; } = new() { Regions = Array.Empty<OcrRegion>() };
}
