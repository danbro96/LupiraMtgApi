using System.Diagnostics;
using LupiraMtgApi.Services.Ocr;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace LupiraMtgApi.Services.Recognition.Pipeline;

/// <summary>
/// Pure helpers extracted from the procedural ScanHandler. Stateless, no DI;
/// usable from any step.
/// </summary>
internal static class ScanHelpers
{
    public static (int Width, int Height) ProbeImageSize(byte[] imageBytes)
    {
        try
        {
            var info = Image.Identify(imageBytes);
            return (info.Width, info.Height);
        }
        catch
        {
            return (0, 0);
        }
    }

    public static async Task<byte[]> Rotate180Async(byte[] bytes, CancellationToken ct)
    {
        await using var input = new MemoryStream(bytes, writable: false);
        using var img = await Image.LoadAsync<Rgba32>(input, ct);
        img.Mutate(c => c.Rotate(RotateMode.Rotate180));

        await using var output = new MemoryStream();
        await img.SaveAsJpegAsync(output, ct);
        return output.ToArray();
    }

    /// <summary>
    /// Counts zones that have meaningful content. The Name 3-char floor and RulesText
    /// 12-char floor mirror the cutoffs used elsewhere — short strings on those zones
    /// are usually OCR noise rather than real card text.
    /// </summary>
    public static int ZoneCoverageScore(CardZones zones)
    {
        var score = 0;
        if (!string.IsNullOrWhiteSpace(zones.Name) && zones.Name.Trim().Length >= 3)
        {
            score++;
        }

        if (!string.IsNullOrWhiteSpace(zones.TypeLine))
        {
            score++;
        }

        if (!string.IsNullOrWhiteSpace(zones.RulesText) && zones.RulesText.Trim().Length >= 12)
        {
            score++;
        }

        if (!string.IsNullOrWhiteSpace(zones.PowerToughness))
        {
            score++;
        }

        if (!string.IsNullOrWhiteSpace(zones.BottomMetadata))
        {
            score++;
        }

        return score;
    }

    public static string BuildScanObjectKey(Guid ownerId, DateTimeOffset scannedAt, Guid scanId, string mediaType)
    {
        var ext = mediaType switch
        {
            "image/png" => "png",
            "image/webp" => "webp",
            _ => "jpg",
        };
        return $"scans/{ownerId:N}/{scannedAt:yyyy}/{scannedAt:MM}/{scanId:N}.{ext}";
    }

    public static string? NullIfEmpty(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    /// <summary>
    /// Picks image dimensions to feed the zone classifier. Florence reports the dims it
    /// actually OCR'd against; if those differ from the dims we computed locally by more
    /// than 5% on either axis the upstream resized our image (e.g. enforced max edge),
    /// and the OCR boxes are in Florence's coordinate space — so we must use Florence's
    /// dims or every centroid lands in the wrong zone band. Tags divergence on the span
    /// so we can spot the resize ratio in telemetry.
    /// </summary>
    public static (int Width, int Height) PickOcrDims(OcrRegions regions, int fallbackWidth, int fallbackHeight, Activity? span)
    {
        var fw = regions.ImageWidth;
        var fh = regions.ImageHeight;
        if (fw <= 0 || fh <= 0)
        {
            return (fallbackWidth, fallbackHeight);
        }

        if (fallbackWidth > 0 && fallbackHeight > 0)
        {
            var widthDelta = Math.Abs(fw - fallbackWidth) / (double) fallbackWidth;
            var heightDelta = Math.Abs(fh - fallbackHeight) / (double) fallbackHeight;
            if (widthDelta > 0.05 || heightDelta > 0.05)
            {
                span?.SetTag("ocr.image_size_divergence", true);
                span?.SetTag("ocr.image_width.local", fallbackWidth);
                span?.SetTag("ocr.image_height.local", fallbackHeight);
                span?.SetTag("ocr.image_width.florence", fw);
                span?.SetTag("ocr.image_height.florence", fh);
            }
        }

        return (fw, fh);
    }
}
