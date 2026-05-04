namespace LupiraMtgApi.Services.Ocr;

public sealed class OcrRegion
{
    public required string Text { get; set; }

    // 8 floats: x1,y1,x2,y2,x3,y3,x4,y4 — Florence's quad_box format.
    public required double[] QuadBox { get; set; }
}