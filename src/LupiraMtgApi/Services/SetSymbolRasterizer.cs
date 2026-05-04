using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SkiaSharp;
using Svg.Skia;

namespace LupiraMtgApi.Services;

public sealed class SetSymbolRasterizer
{
    public const int RasterSize = 128;

    // Pixels darker than this fraction of full luminance become "set" in the silhouette.
    // Tuned to capture both natural-color Scryfall icon SVGs (typically black) and the
    // rarity-tinted symbols seen on actual cards (silver/gold/orange) — all generally
    // sit below ~0.65 on a white background.
    private const double LuminanceThreshold = 0.65;

    private readonly PHashService pHash;
    private readonly ILogger<SetSymbolRasterizer> logger;

    public SetSymbolRasterizer(PHashService pHash, ILogger<SetSymbolRasterizer> logger)
    {
        this.pHash = pHash;
        this.logger = logger;
    }

    public async Task<RasterizedSymbol> RasterizeAsync(Stream svgStream, CancellationToken ct)
    {
        using var skSvg = new SKSvg();
        var picture = skSvg.Load(svgStream) ?? throw new InvalidOperationException("Failed to load SVG.");

        var bounds = picture.CullRect;
        if (bounds.Width <= 0 || bounds.Height <= 0)
        {
            throw new InvalidOperationException("SVG has no rendering bounds.");
        }

        var info = new SKImageInfo(RasterSize, RasterSize, SKColorType.Rgba8888, SKAlphaType.Unpremul);
        using var bitmap = new SKBitmap(info);
        using (var canvas = new SKCanvas(bitmap))
        {
            canvas.Clear(SKColors.White);
            var scale = Math.Min(RasterSize / bounds.Width, RasterSize / bounds.Height);
            var dx = ((RasterSize - (bounds.Width * scale)) / 2f) - (bounds.Left * scale);
            var dy = ((RasterSize - (bounds.Height * scale)) / 2f) - (bounds.Top * scale);
            canvas.Translate(dx, dy);
            canvas.Scale(scale);
            canvas.DrawPicture(picture);
        }

        return await BinarizeAndHashAsync(bitmap.Bytes, RasterSize, RasterSize, ct);
    }

    public async Task<RasterizedSymbol> BinarizeAndHashAsync(
        byte[] rgbaPixels,
        int width,
        int height,
        CancellationToken ct)
    {
        if (rgbaPixels.Length != width * height * 4)
        {
            throw new ArgumentException(
                $"Pixel buffer size {rgbaPixels.Length} does not match {width}x{height}x4.",
                nameof(rgbaPixels));
        }

        using var img = Image.LoadPixelData<Rgba32>(rgbaPixels, width, height);
        Binarize(img);

        await using var ms = new MemoryStream();
        await img.SaveAsPngAsync(ms, ct);
        var pngBytes = ms.ToArray();

        ms.Position = 0;
        var hash = this.pHash.Compute(ms);

        return new RasterizedSymbol { PngBytes = pngBytes, PHash = hash };
    }

    private static void Binarize(Image<Rgba32> img)
    {
        img.ProcessPixelRows(accessor =>
        {
            for (var y = 0; y < accessor.Height; y++)
            {
                var row = accessor.GetRowSpan(y);
                for (var x = 0; x < row.Length; x++)
                {
                    var p = row[x];
                    var luminance = ((0.299 * p.R) + (0.587 * p.G) + (0.114 * p.B)) / 255.0;
                    row[x] = luminance < LuminanceThreshold
                        ? new Rgba32(0, 0, 0, 255)
                        : new Rgba32(255, 255, 255, 255);
                }
            }
        });
    }
}

public sealed class RasterizedSymbol
{
    public required byte[] PngBytes { get; set; }

    public required long PHash { get; set; }
}
