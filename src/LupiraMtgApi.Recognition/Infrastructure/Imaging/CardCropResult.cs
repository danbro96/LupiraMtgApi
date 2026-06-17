namespace LupiraMtgApi.Recognition.Infrastructure.Imaging;

public sealed class CardCropResult
{
    public required byte[] Bytes { get; set; }

    public required string MediaType { get; set; }

    public required bool IsCropped { get; set; }

    public required double CropConfidence { get; set; }

    public required int Width { get; set; }

    public required int Height { get; set; }

    // True when the cropped bbox came out landscape and we rotated 90° to portrait.
    // The card was photographed sideways. Surfaced in ScanDebug for telemetry.
    public bool Rotated { get; set; }
}