using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats;
using SixLabors.ImageSharp.Formats.Jpeg;
using SixLabors.ImageSharp.Formats.Png;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace LupiraMtgApi.Services.Imaging;

public sealed class CardCropService
{
    // Detection runs on a downsampled copy to keep latency stable on large phone photos.
    private const int DetectionLongEdge = 1024;

    // Trim cumulative edge mass from each side until this fraction is reached. The same
    // fraction is left at the opposite end → bbox holds (1 - 2*Cutoff) of total edge mass.
    private const double EdgeMassCutoff = 0.025;

    // Minimum fused confidence for emitting a crop (size_factor * density_factor).
    private const double MinCropConfidence = 0.55;

    public async Task<CardCropResult> PreprocessAsync(byte[] imageBytes, string mediaType, CancellationToken ct)
    {
        var contentType = string.IsNullOrWhiteSpace(mediaType) ? "image/jpeg" : mediaType;

        await using var inputStream = new MemoryStream(imageBytes, writable: false);
        using var original = await Image.LoadAsync<Rgba32>(inputStream, ct);
        var origW = original.Width;
        var origH = original.Height;

        using var detection = original.Clone();
        var scale = (double) DetectionLongEdge / Math.Max(origW, origH);
        if (scale < 1.0)
        {
            var dw = Math.Max(1, (int) (origW * scale));
            var dh = Math.Max(1, (int) (origH * scale));
            detection.Mutate(x => x.Resize(dw, dh));
        }

        detection.Mutate(x => x
            .Grayscale()
            .BoxBlur(1)
            .DetectEdges(KnownEdgeDetectorKernels.Sobel));

        var detW = detection.Width;
        var detH = detection.Height;
        var rowSum = new long[detH];
        var colSum = new long[detW];
        long totalSum = 0;

        detection.ProcessPixelRows(accessor =>
        {
            for (var y = 0; y < accessor.Height; y++)
            {
                var row = accessor.GetRowSpan(y);
                long rsum = 0;
                for (var x = 0; x < row.Length; x++)
                {
                    int v = row[x].R;
                    rsum += v;
                    colSum[x] += v;
                }

                rowSum[y] = rsum;
                totalSum += rsum;
            }
        });

        if (totalSum == 0)
        {
            return Uncropped(imageBytes, contentType, origW, origH, cropConfidence: 0.0);
        }

        var massCutoff = (long) (totalSum * EdgeMassCutoff);

        var (yTop, yBot) = TrimToMassBounds(rowSum, massCutoff);
        var (xLeft, xRight) = TrimToMassBounds(colSum, massCutoff);

        if (yBot <= yTop || xRight <= xLeft)
        {
            return Uncropped(imageBytes, contentType, origW, origH, cropConfidence: 0.0);
        }

        var boxW = xRight - xLeft + 1;
        var boxH = yBot - yTop + 1;

        long insideSum = 0;
        detection.ProcessPixelRows(accessor =>
        {
            for (var y = yTop; y <= yBot; y++)
            {
                var row = accessor.GetRowSpan(y);
                for (var x = xLeft; x <= xRight; x++)
                {
                    insideSum += row[x].R;
                }
            }
        });

        var insideMean = (double) insideSum / (boxW * (double) boxH);
        var totalMean = (double) totalSum / (detW * (double) detH);
        var sizeFactor = (double) Math.Min(boxW, boxH) / Math.Min(detW, detH);
        var densityFactor = totalMean > 0 ? insideMean / totalMean : 0.0;
        var confidence = Math.Clamp(sizeFactor * densityFactor, 0.0, 1.0);

        if (confidence < MinCropConfidence)
        {
            return Uncropped(imageBytes, contentType, origW, origH, confidence);
        }

        // Map detection-space bbox back to the original image.
        var sx = (double) origW / detW;
        var sy = (double) origH / detH;
        var oxLeft = Math.Max(0, (int) (xLeft * sx));
        var oyTop = Math.Max(0, (int) (yTop * sy));
        var oxRight = Math.Min(origW - 1, (int) Math.Ceiling((xRight + 1) * sx) - 1);
        var oyBot = Math.Min(origH - 1, (int) Math.Ceiling((yBot + 1) * sy) - 1);
        var origBoxW = oxRight - oxLeft + 1;
        var origBoxH = oyBot - oyTop + 1;

        using var cropped = original.Clone(ctx => ctx.Crop(new Rectangle(oxLeft, oyTop, origBoxW, origBoxH)));

        // MTG cards are portrait. If the bbox came out landscape (W > H), the card was
        // photographed sideways and Florence will have read text upright but in a frame
        // rotated 90° from the portrait reference our zone bands assume. Rotate back to
        // portrait so the downstream classifier's y-bands line up with the card.
        // Direction defaults to clockwise; if telemetry shows it's wrong half the time
        // we can re-orient using OCR centroid distribution as a second pass.
        var rotated = false;
        var outputW = origBoxW;
        var outputH = origBoxH;
        if (origBoxW > origBoxH)
        {
            cropped.Mutate(c => c.Rotate(RotateMode.Rotate90));
            outputW = origBoxH;
            outputH = origBoxW;
            rotated = true;
        }

        var (encoder, outMediaType) = SelectEncoder(contentType);
        using var ms = new MemoryStream();
        await cropped.SaveAsync(ms, encoder, ct);

        return new CardCropResult
        {
            Bytes = ms.ToArray(),
            MediaType = outMediaType,
            IsCropped = true,
            CropConfidence = confidence,
            Width = outputW,
            Height = outputH,
            Rotated = rotated,
        };
    }

    private static (int Lo, int Hi) TrimToMassBounds(long[] axis, long massCutoff)
    {
        var n = axis.Length;
        var lo = 0;
        long acc = 0;
        while (lo < n - 1 && acc + axis[lo] < massCutoff)
        {
            acc += axis[lo];
            lo++;
        }

        var hi = n - 1;
        acc = 0;
        while (hi > lo && acc + axis[hi] < massCutoff)
        {
            acc += axis[hi];
            hi--;
        }

        return (lo, hi);
    }

    private static CardCropResult Uncropped(byte[] bytes, string mediaType, int width, int height, double cropConfidence)
    {
        return new CardCropResult
        {
            Bytes = bytes,
            MediaType = mediaType,
            IsCropped = false,
            CropConfidence = cropConfidence,
            Width = width,
            Height = height,
        };
    }

    private static (IImageEncoder Encoder, string MediaType) SelectEncoder(string mediaType)
    {
        if (mediaType.Contains("png", StringComparison.OrdinalIgnoreCase))
        {
            return (new PngEncoder(), "image/png");
        }

        return (new JpegEncoder { Quality = 92 }, "image/jpeg");
    }
}
