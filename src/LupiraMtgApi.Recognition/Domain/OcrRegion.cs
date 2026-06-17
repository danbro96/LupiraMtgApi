namespace LupiraMtgApi.Recognition.Domain;

public sealed class OcrRegion
{
    public required string Text { get; set; }

    /// <summary>8 floats: x1,y1,x2,y2,x3,y3,x4,y4 — Florence's quad polygon, ordered TL,TR,BR,BL. Null when Florence omitted it.</summary>
    public double[]? Quad { get; set; }

    /// <summary>Axis-aligned bounding box in image pixels.</summary>
    public required BoundingBox Box { get; set; }

    /// <summary>Top-edge angle in degrees, range (-180, 180]; ~0 for upright text, ~±180 for upside-down.</summary>
    public required double Rotation { get; set; }

    /// <summary>Mean per-token probability of this region's label tokens, in [0, 1]. Relative ranking signal.</summary>
    public required double Confidence { get; set; }
}
