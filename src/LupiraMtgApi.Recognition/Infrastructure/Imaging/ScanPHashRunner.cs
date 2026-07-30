using System.Diagnostics;
using LupiraMtgApi.Recognition.Application.Pipeline;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace LupiraMtgApi.Recognition.Infrastructure.Imaging;

/// <summary>
/// Computes pHashes against both the art rectangle and the full card image, optionally
/// trying a 180° rotation when the cropper had to rotate the input. Searches both
/// BK-trees and merges hits per printing by minimum hamming distance. Encapsulates
/// the previously-inline <c>RunPHashAsync</c> logic from ScanHandler.
/// </summary>
public sealed class ScanPHashRunner
{
    // Modern-frame art rectangle, in card-relative coords. See ScanHandler comments
    // for the alignment rationale; these values were tuned in round 2.
    private const double ArtCropYMin = 0.08;
    private const double ArtCropYMax = 0.575;
    private const double ArtCropXMin = 0.05;
    private const double ArtCropXMax = 0.95;

    private readonly PHashIndex _pHashIndex;
    private readonly FullCardPHashIndex _fullCardPHashIndex;
    private readonly PHashService _pHash;
    private readonly ScanScoringOptions _scoring;
    private readonly ILogger<ScanPHashRunner> _logger;

    public ScanPHashRunner(
        PHashIndex pHashIndex,
        FullCardPHashIndex fullCardPHashIndex,
        PHashService pHash,
        IOptions<ScanScoringOptions> scoring,
        ILogger<ScanPHashRunner> logger)
    {
        _pHashIndex = pHashIndex;
        _fullCardPHashIndex = fullCardPHashIndex;
        _pHash = pHash;
        _scoring = scoring.Value;
        _logger = logger;
    }

    public Task<PHashResult> RunAsync(byte[] imageBytes, Guid scanId, bool tryAltRotation)
    {
        // Capture the parent activity context so the Task.Run continuation parents its
        // span under the root scan span.
        var parent = Activity.Current;
        return Task.Run(() =>
        {
            using var span = ScanTelemetry.Source.StartActivity("phash.compute", ActivityKind.Internal, parent?.Context ?? default);
            var artLoaded = _pHashIndex.IsLoaded;
            var fullLoaded = _fullCardPHashIndex.IsLoaded;
            if (!artLoaded && !fullLoaded)
            {
                span?.SetTag("phash.art_index_loaded", false);
                span?.SetTag("phash.full_index_loaded", false);
                return PHashResult.Empty;
            }

            span?.SetTag("phash.art_index_loaded", artLoaded);
            span?.SetTag("phash.full_index_loaded", fullLoaded);
            span?.SetTag("phash.art_index_size", _pHashIndex.Count);
            span?.SetTag("phash.full_index_size", _fullCardPHashIndex.Count);

            var fullCardHamming = _scoring.FullCardPHashMaxHamming;
            var artHamming = _scoring.PHashMaxHamming;
            var topK = _scoring.PHashTopK;
            var sw = Stopwatch.StartNew();
            try
            {
                using var stream = new MemoryStream(imageBytes);
                using var fullImg = Image.Load<Rgba32>(stream);

                // Full-card pHash uses the WHOLE cropped image — no rectangle extraction.
                var (fullHash, fullHits, _, fullRotation) = ComputeAndSearch(
                    fullImg,
                    tryAltRotation,
                    h => _fullCardPHashIndex.Search(h, fullCardHamming).Take(topK).ToList());

                // Art-only pHash. Re-decode from the original buffer.
                stream.Position = 0;
                using var artImg = Image.Load<Rgba32>(stream);

                var x = (int) Math.Round(artImg.Width * ArtCropXMin);
                var y = (int) Math.Round(artImg.Height * ArtCropYMin);
                var w = (int) Math.Round(artImg.Width * (ArtCropXMax - ArtCropXMin));
                var h = (int) Math.Round(artImg.Height * (ArtCropYMax - ArtCropYMin));
                var artExtracted = w >= 32 && h >= 32 && x >= 0 && y >= 0 && x + w <= artImg.Width && y + h <= artImg.Height;
                if (artExtracted)
                {
                    artImg.Mutate(ctx => ctx.Crop(new Rectangle(x, y, w, h)));
                }

                span?.SetTag("phash.art_extracted", artExtracted);

                var (artHash, artHits, _, artRotation) = ComputeAndSearch(
                    artImg,
                    tryAltRotation,
                    hh => _pHashIndex.Search(hh, artHamming).Take(topK).ToList());

                // Merge: per-printing minimum hamming across both indexes.
                var merged = new Dictionary<string, int>(StringComparer.Ordinal);
                foreach (var hit in artHits)
                {
                    merged[hit.PrintingId] = hit.Distance;
                }

                foreach (var hit in fullHits)
                {
                    if (merged.TryGetValue(hit.PrintingId, out var existing))
                    {
                        if (hit.Distance < existing)
                        {
                            merged[hit.PrintingId] = hit.Distance;
                        }
                    }
                    else
                    {
                        merged[hit.PrintingId] = hit.Distance;
                    }
                }

                var hits = merged
                    .Select(kvp => new PHashIndex.PHashHit(kvp.Key, kvp.Value))
                    .OrderBy(hh => hh.Distance)
                    .Take(topK)
                    .ToList();

                var winningSource = "neither";
                if (hits.Count > 0)
                {
                    var topId = hits[0].PrintingId;
                    var inArt = artHits.Any(hh => hh.PrintingId == topId);
                    var inFull = fullHits.Any(hh => hh.PrintingId == topId);
                    winningSource = (inArt, inFull) switch
                    {
                        (true, true) => "both",
                        (true, false) => "art",
                        (false, true) => "full",
                        _ => "neither",
                    };
                }

                span?.SetTag("phash.art_hit_count", artHits.Count);
                span?.SetTag("phash.art_best_hamming", artHits.Count > 0 ? artHits[0].Distance : -1);
                span?.SetTag("phash.art_winning_rotation", artRotation);
                span?.SetTag("phash.art_hash", artHash);
                span?.SetTag("phash.full_hit_count", fullHits.Count);
                span?.SetTag("phash.full_best_hamming", fullHits.Count > 0 ? fullHits[0].Distance : -1);
                span?.SetTag("phash.full_winning_rotation", fullRotation);
                span?.SetTag("phash.full_hash", fullHash);
                span?.SetTag("phash.merged_hit_count", hits.Count);
                span?.SetTag("phash.winning_source", winningSource);

                ScanTelemetry.FullPhashCandidateCount.Record(fullHits.Count);
                ScanTelemetry.WinningPhashSource.Add(1, new KeyValuePair<string, object?>("source", winningSource));

                sw.Stop();
                return new PHashResult(artHash, (int) sw.ElapsedMilliseconds, hits);
            }
            catch (Exception ex)
            {
                sw.Stop();
                _logger.LogWarning(ex, "pHash compute failed for scan {ScanId}; falling back to OCR-only candidates", scanId);
                span?.SetTag("error.type", ex.GetType().Name);
                ScanTelemetry.PhashFailures.Add(1, new KeyValuePair<string, object?>("error.type", ex.GetType().Name));
                return new PHashResult(null, (int) sw.ElapsedMilliseconds, Array.Empty<PHashIndex.PHashHit>());
            }
        });
    }

    /// <summary>
    /// Hashes an image, optionally also its 180° rotation, and returns whichever
    /// side produced the lower best-hamming hit.
    /// </summary>
    private (long Hash, IReadOnlyList<PHashIndex.PHashHit> Hits, int BestHamming, string WinningRotation) ComputeAndSearch(
        Image<Rgba32> img,
        bool tryAltRotation,
        Func<long, IReadOnlyList<PHashIndex.PHashHit>> search)
    {
        var primaryHash = _pHash.Compute(img);
        var primaryHits = search(primaryHash);
        var primaryBest = primaryHits.Count > 0 ? primaryHits[0].Distance : int.MaxValue;

        if (!tryAltRotation)
        {
            return (primaryHash, primaryHits, primaryBest, "primary");
        }

        img.Mutate(ctx => ctx.Rotate(RotateMode.Rotate180));
        var altHash = _pHash.Compute(img);
        var altHits = search(altHash);
        var altBest = altHits.Count > 0 ? altHits[0].Distance : int.MaxValue;

        if (altBest < primaryBest)
        {
            return (altHash, altHits, altBest, "alt_180");
        }

        return (primaryHash, primaryHits, primaryBest, "primary");
    }
}

public readonly record struct PHashResult(long? Hash, int LatencyMs, IReadOnlyList<PHashIndex.PHashHit> Hits)
{
    public static PHashResult Empty => new(null, 0, Array.Empty<PHashIndex.PHashHit>());
}
