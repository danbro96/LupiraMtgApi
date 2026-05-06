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
}
