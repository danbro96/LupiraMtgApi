namespace LupiraMtgApi.Services.SetSymbol;

public sealed class RasterizedSymbol
{
    public required byte[] PngBytes { get; set; }

    public required long PHash { get; set; }
}