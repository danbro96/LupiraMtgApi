namespace LupiraMtgApi.Services;

public sealed class OcrRegions
{
    public required IReadOnlyList<OcrRegion> Regions { get; set; }

    public static OcrRegions Empty { get; } = new() { Regions = Array.Empty<OcrRegion>() };
}

public sealed class OcrRegion
{
    public required string Text { get; set; }

    // 8 floats: x1,y1,x2,y2,x3,y3,x4,y4 — Florence's quad_box format.
    public required double[] QuadBox { get; set; }
}
