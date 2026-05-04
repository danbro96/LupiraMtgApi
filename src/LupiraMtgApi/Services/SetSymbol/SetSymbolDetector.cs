using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;
using LupiraMtgApi.Services.Recognition;
namespace LupiraMtgApi.Services.SetSymbol;

public sealed class SetSymbolDetector
{
    // Proportional band where the symbol sits on a normalized cropped card. Slightly wider
    // than the TypeLine right-side region used by CardZoneClassifier — symbol horizontal
    // extent varies more across sets than the type-line text does.
    private const double SymbolYLo = 0.545;
    private const double SymbolYHi = 0.610;
    private const double SymbolXLo = 0.77;
    private const double SymbolXHi = 0.94;

    private const int RasterSize = SetSymbolRasterizer.RasterSize;
    private const int MinPatchEdgePixels = 32;
    private const int MaxHamming = 14;

    private readonly SetSymbolRasterizer _rasterizer;
    private readonly SetSymbolIndex _index;
    private readonly ILogger<SetSymbolDetector> _logger;

    public SetSymbolDetector(
        SetSymbolRasterizer rasterizer,
        SetSymbolIndex index,
        ILogger<SetSymbolDetector> logger)
    {
        _rasterizer = rasterizer;
        _index = index;
        _logger = logger;
    }

    public async Task<SetSymbolMatch?> DetectAsync(byte[] croppedBytes, string mediaType, CancellationToken ct)
    {
        if (!_index.IsLoaded)
        {
            return null;
        }

        try
        {
            await using var inputStream = new MemoryStream(croppedBytes, writable: false);
            using var img = await Image.LoadAsync<Rgba32>(inputStream, ct);

            var x = (int) Math.Round(img.Width * SymbolXLo);
            var y = (int) Math.Round(img.Height * SymbolYLo);
            var w = (int) Math.Round(img.Width * (SymbolXHi - SymbolXLo));
            var h = (int) Math.Round(img.Height * (SymbolYHi - SymbolYLo));

            if (w < MinPatchEdgePixels || h < MinPatchEdgePixels)
            {
                return null;
            }

            using var patch = img.Clone(ctx => ctx
                .Crop(new Rectangle(x, y, w, h))
                .Resize(RasterSize, RasterSize));

            var rgbaBytes = new byte[RasterSize * RasterSize * 4];
            patch.CopyPixelDataTo(rgbaBytes);

            var raster = await _rasterizer.BinarizeAndHashAsync(rgbaBytes, RasterSize, RasterSize, ct);

            var hits = _index.Search(raster.PHash, MaxHamming);
            if (hits.Count == 0)
            {
                return null;
            }

            var best = hits[0];
            return new SetSymbolMatch
            {
                SetCode = best.SetCode,
                HammingDistance = best.Distance,
                Score = Math.Clamp(1.0 - (best.Distance / 64.0), 0.0, 1.0),
            };
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Set-symbol detection failed; ignoring symbol signal");
            return null;
        }
    }
}
