namespace LupiraMtgApi.Services.Ocr;

public sealed class BoundingBox
{
    public required double XMin { get; set; }

    public required double YMin { get; set; }

    public required double XMax { get; set; }

    public required double YMax { get; set; }

    public double CenterX => (this.XMin + this.XMax) / 2.0;

    public double CenterY => (this.YMin + this.YMax) / 2.0;

    public double Area => Math.Max(0.0, this.XMax - this.XMin) * Math.Max(0.0, this.YMax - this.YMin);
}
