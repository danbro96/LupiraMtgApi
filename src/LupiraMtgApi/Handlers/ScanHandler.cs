using System.Diagnostics;
using LupiraMtgApi.Data;
using LupiraMtgApi.Models;
using LupiraMtgApi.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using SixLabors.ImageSharp;

namespace LupiraMtgApi.Handlers;

public sealed class ScanHandler
{
    private const int MaxImageBytes = 4 * 1024 * 1024;
    private const int PHashTopK = 10;
    private const int FinalTopN = 5;
    private const int PHashMaxHamming = 12;

    private readonly LupiraMtgDbContext db;
    private readonly PHashIndex pHashIndex;
    private readonly PHashService pHash;
    private readonly IOcrService ocr;
    private readonly CardCropService crop;
    private readonly CardZoneClassifier zoneClassifier;
    private readonly CardZoneScorer zoneScorer;
    private readonly CardPrintingMapper mapper;
    private readonly ScanScoringOptions scoring;
    private readonly ILogger<ScanHandler> logger;

    public ScanHandler(
        LupiraMtgDbContext db,
        PHashIndex pHashIndex,
        PHashService pHash,
        IOcrService ocr,
        CardCropService crop,
        CardZoneClassifier zoneClassifier,
        CardZoneScorer zoneScorer,
        CardPrintingMapper mapper,
        IOptions<ScanScoringOptions> scoring,
        ILogger<ScanHandler> logger)
    {
        this.db = db;
        this.pHashIndex = pHashIndex;
        this.pHash = pHash;
        this.ocr = ocr;
        this.crop = crop;
        this.zoneClassifier = zoneClassifier;
        this.zoneScorer = zoneScorer;
        this.mapper = mapper;
        this.scoring = scoring.Value;
        this.logger = logger;
    }

    public async Task<Results<Ok<ScanResponse>, BadRequest<string>>> ScanAsync(
        IFormFile image,
        CancellationToken ct)
    {
        if (image is null || image.Length == 0)
        {
            return TypedResults.BadRequest("Image file is required.");
        }

        if (image.Length > MaxImageBytes)
        {
            return TypedResults.BadRequest($"Image is too large; max {MaxImageBytes} bytes.");
        }

        byte[] imageBytes;
        await using (var ms = new MemoryStream(capacity: (int)image.Length))
        {
            await image.CopyToAsync(ms, ct);
            imageBytes = ms.ToArray();
        }

        var inputMediaType = string.IsNullOrEmpty(image.ContentType) ? "image/jpeg" : image.ContentType;

        CardCropResult preprocessed;
        try
        {
            preprocessed = await this.crop.PreprocessAsync(imageBytes, inputMediaType, ct);
        }
        catch (Exception ex)
        {
            this.logger.LogWarning(ex, "Card crop preprocessing failed; continuing with original image");
            var (fallbackW, fallbackH) = ProbeImageSize(imageBytes);
            preprocessed = new CardCropResult
            {
                Bytes = imageBytes,
                MediaType = inputMediaType,
                Cropped = false,
                CropConfidence = 0.0,
                Width = fallbackW,
                Height = fallbackH,
            };
        }

        var pHashTask = this.RunPHashAsync(preprocessed.Bytes);
        var ocrTask = this.RunOcrRegionsAsync(preprocessed.Bytes, preprocessed.MediaType, ct);

        await Task.WhenAll(pHashTask, ocrTask);
        var (imageHash, pHashLatencyMs, pHashHits) = pHashTask.Result;
        var (regions, ocrLatencyMs) = ocrTask.Result;

        var zones = preprocessed.Width > 0 && preprocessed.Height > 0
            ? this.zoneClassifier.Classify(regions, preprocessed.Width, preprocessed.Height, preprocessed.Cropped)
            : CardZones.Empty;

        var scoringResult = await this.zoneScorer.ScoreAsync(zones, ct);

        var byPrinting = new Dictionary<string, FinalRow>(StringComparer.Ordinal);
        foreach (var (id, scores) in scoringResult.ByPrinting)
        {
            byPrinting[id] = new FinalRow { PrintingId = id, ZoneScores = scores };
        }

        foreach (var hit in pHashHits)
        {
            if (!byPrinting.TryGetValue(hit.PrintingId, out var row))
            {
                row = new FinalRow { PrintingId = hit.PrintingId };
                byPrinting[hit.PrintingId] = row;
            }

            row.HammingDistance = hit.Distance;
            row.HammingScore = Math.Clamp(1.0 - (hit.Distance / 64.0), 0.0, 1.0);
        }

        var ocrSignalAvailable = scoringResult.Weights.TotalPresent > 0;
        foreach (var row in byPrinting.Values)
        {
            var ocrScore = row.ZoneScores?.AggregateScore ?? 0.0;
            var (wp, wo) = SelectFusionWeights(row.HammingDistance.HasValue, ocrSignalAvailable);
            row.FinalScore = Math.Clamp((wp * row.HammingScore) + (wo * ocrScore), 0.0, 1.0);
        }

        var top = byPrinting.Values
            .OrderByDescending(r => r.FinalScore)
            .Take(FinalTopN)
            .ToList();

        var (ranked, hydratedRows) = await this.HydrateCandidatesAsync(top, ct);
        var confidence = this.ClassifyConfidence(ranked, hydratedRows);

        return TypedResults.Ok(new ScanResponse
        {
            Confidence = confidence,
            Candidates = ranked,
            Debug = new ScanDebug
            {
                Zones = new ScanZoneTexts
                {
                    Name = zones.Name,
                    TypeLine = zones.TypeLine,
                    RulesText = zones.RulesText,
                    PowerToughness = zones.PowerToughness,
                    BottomMetadata = zones.BottomMetadata,
                },
                ImagePHash = imageHash,
                Cropped = preprocessed.Cropped,
                CropConfidence = preprocessed.CropConfidence,
                CroppedWidth = preprocessed.Width,
                CroppedHeight = preprocessed.Height,
                OcrRegionCount = regions.Regions.Count,
                PHashCandidateCount = pHashHits.Count,
                OcrCandidateCount = scoringResult.ByPrinting.Count,
                OcrLatencyMs = ocrLatencyMs,
                PHashLatencyMs = pHashLatencyMs,
            },
        });
    }

    private async Task<(List<CardCandidateResponse> Ranked, List<FinalRow> HydratedRows)> HydrateCandidatesAsync(
        List<FinalRow> top,
        CancellationToken ct)
    {
        if (top.Count == 0)
        {
            return (new List<CardCandidateResponse>(), new List<FinalRow>());
        }

        var topIds = top.Select(r => r.PrintingId).ToList();
        var printings = await this.db.CardPrintings
            .AsNoTracking()
            .Where(p => topIds.Contains(p.Id))
            .ToListAsync(ct);
        var printingsById = printings.ToDictionary(p => p.Id, StringComparer.Ordinal);

        var setCodes = printings.Select(p => p.SetCode).Distinct().ToList();
        var setNames = await this.db.Sets
            .AsNoTracking()
            .Where(s => setCodes.Contains(s.Code))
            .ToDictionaryAsync(s => s.Code, s => s.Name, ct);

        var ranked = new List<CardCandidateResponse>(top.Count);
        var hydratedRows = new List<FinalRow>(top.Count);
        foreach (var row in top)
        {
            if (!printingsById.TryGetValue(row.PrintingId, out var printing))
            {
                continue;
            }

            var setName = setNames.GetValueOrDefault(printing.SetCode, printing.SetCode);
            var printingResponse = await this.mapper.MapAsync(printing, setName, ct);

            ranked.Add(new CardCandidateResponse
            {
                Printing = printingResponse,
                CombinedScore = row.FinalScore,
                OcrAggregateScore = row.ZoneScores?.AggregateScore ?? 0.0,
                NameScore = row.ZoneScores?.NameScore ?? 0.0,
                TypeLineScore = row.ZoneScores?.TypeLineScore ?? 0.0,
                RulesTextScore = row.ZoneScores?.RulesTextScore ?? 0.0,
                PowerToughnessScore = row.ZoneScores?.PowerToughnessScore ?? 0.0,
                BottomMetadataScore = row.ZoneScores?.BottomMetadataScore ?? 0.0,
                HammingScore = row.HammingScore,
                HammingDistance = row.HammingDistance,
                MatchedByPHash = row.HammingDistance.HasValue,
                MatchedByName = (row.ZoneScores?.NameScore ?? 0.0) > 0,
            });
            hydratedRows.Add(row);
        }

        return (ranked, hydratedRows);
    }

    private Task<(long? Hash, int LatencyMs, IReadOnlyList<PHashIndex.PHashHit> Hits)> RunPHashAsync(byte[] imageBytes)
    {
        return Task.Run(() =>
        {
            if (!this.pHashIndex.IsLoaded)
            {
                return ((long?)null, 0, (IReadOnlyList<PHashIndex.PHashHit>)Array.Empty<PHashIndex.PHashHit>());
            }

            var sw = Stopwatch.StartNew();
            using var stream = new MemoryStream(imageBytes);
            long hash;
            try
            {
                hash = this.pHash.Compute(stream);
            }
            catch (Exception ex)
            {
                sw.Stop();
                this.logger.LogWarning(ex, "pHash compute failed; falling back to OCR-only candidates");
                return ((long?)null, (int)sw.ElapsedMilliseconds, (IReadOnlyList<PHashIndex.PHashHit>)Array.Empty<PHashIndex.PHashHit>());
            }

            var hits = this.pHashIndex.Search(hash, PHashMaxHamming).Take(PHashTopK).ToList();
            sw.Stop();
            return ((long?)hash, (int)sw.ElapsedMilliseconds, (IReadOnlyList<PHashIndex.PHashHit>)hits);
        });
    }

    private async Task<(OcrRegions Regions, int LatencyMs)> RunOcrRegionsAsync(byte[] imageBytes, string mediaType, CancellationToken ct)
    {
        var sw = Stopwatch.StartNew();
        try
        {
            var regions = await this.ocr.ReadRegionsAsync(imageBytes, mediaType, ct);
            sw.Stop();
            return (regions, (int)sw.ElapsedMilliseconds);
        }
        catch (Exception ex)
        {
            sw.Stop();
            this.logger.LogWarning(ex, "OCR regions call failed; falling back to pHash-only candidates");
            return (OcrRegions.Empty, (int)sw.ElapsedMilliseconds);
        }
    }

    // When only one of pHash/OCR contributes, scale that signal's weight to 1.0.
    // Without this, a perfect single-signal match could not exceed PHashWeight or OcrWeight
    // (typically 0.45/0.55 → < MediumCombined). Mirrors the per-zone re-normalization
    // inside CardZoneScorer.
    private (double PHashWeight, double OcrWeight) SelectFusionWeights(bool hasPhash, bool hasOcr)
    {
        return (hasPhash, hasOcr) switch
        {
            (true, true) => (this.scoring.PHashWeight, this.scoring.OcrWeight),
            (true, false) => (1.0, 0.0),
            (false, true) => (0.0, 1.0),
            _ => (0.0, 0.0),
        };
    }

    private static (int Width, int Height) ProbeImageSize(byte[] imageBytes)
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

    private RecognitionConfidence ClassifyConfidence(IReadOnlyList<CardCandidateResponse> ranked, IReadOnlyList<FinalRow> rows)
    {
        if (ranked.Count == 0)
        {
            return RecognitionConfidence.Low;
        }

        var best = ranked[0];

        if (best.CombinedScore >= this.scoring.HighCombined && rows.Count > 0)
        {
            var contributing = rows[0].ZoneScores?.ContributingZoneCount(this.scoring.HighZoneAgreementMinScore) ?? 0;
            if (contributing >= this.scoring.HighZoneAgreementMinCount)
            {
                return RecognitionConfidence.High;
            }
        }

        if (best.CombinedScore >= this.scoring.MediumCombined)
        {
            return RecognitionConfidence.Medium;
        }

        return RecognitionConfidence.Low;
    }

    private sealed class FinalRow
    {
        public required string PrintingId { get; set; }

        public PrintingZoneScores? ZoneScores { get; set; }

        public double HammingScore { get; set; }

        public int? HammingDistance { get; set; }

        public double FinalScore { get; set; }
    }
}
