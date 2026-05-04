namespace LupiraMtgApi.Services.Imaging;

public sealed class CardCropResult
{
    public required byte[] Bytes { get; set; }

    public required string MediaType { get; set; }

    public required bool Cropped { get; set; }

    public required double CropConfidence { get; set; }

    public required int Width { get; set; }

    public required int Height { get; set; }
}