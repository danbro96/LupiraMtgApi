using LupiraMtgApi.Data;
using LupiraMtgApi.Domain.ScanLog;
using LupiraMtgApi.Models;
using LupiraMtgApi.Services;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using SixLabors.ImageSharp;
using System.Diagnostics;
using LupiraMtgApi.Models.Scans;
using LupiraMtgApi.Services.Imaging;
using LupiraMtgApi.Services.Ocr;
using LupiraMtgApi.Services.Recognition;
using LupiraMtgApi.Services.Scryfall;
using LupiraMtgApi.Services.SetSymbol;
using LupiraMtgApi.Services.Storage;
namespace LupiraMtgApi.Handlers;

public sealed class ScanHandler
{
    private const int MaxImageBytes = 4 * 1024 * 1024;
    private const int PHashTopK = 10;
    private const int FinalTopN = 5;
    private const int PHashMaxHamming = 12;

    // Used when a printing's set has no matching set_type_weights row (defensive
    // against new Scryfall set_types we haven't seeded). Neutral midpoint so an
    // unknown set still ranks plausibly.
    private const double DefaultSetTypeWeight = 0.5;

    private readonly LupiraMtgDbContext _db;
    private readonly Marten.IDocumentSession _session;
    private readonly IImageStore _images;
    private readonly PHashIndex _pHashIndex;
    private readonly PHashService _pHash;
    private readonly IOcrService _ocr;
    private readonly CardCropService _crop;
    private readonly CardZoneClassifier _zoneClassifier;
    private readonly CardZoneScorer _zoneScorer;
    private readonly SetSymbolDetector _symbolDetector;
    private readonly CardPrintingMapper _mapper;
    private readonly ScanScoringOptions _scoring;
    private readonly ILogger<ScanHandler> _logger;

    public ScanHandler(
        LupiraMtgDbContext db,
        Marten.IDocumentSession session,
        IImageStore images,
        PHashIndex pHashIndex,
        PHashService pHash,
        IOcrService ocr,
        CardCropService crop,
        CardZoneClassifier zoneClassifier,
        CardZoneScorer zoneScorer,
        SetSymbolDetector symbolDetector,
        CardPrintingMapper mapper,
        IOptions<ScanScoringOptions> scoring,
        ILogger<ScanHandler> logger)
    {
        _db = db;
        _session = session;
        _images = images;
        _pHashIndex = pHashIndex;
        _pHash = pHash;
        _ocr = ocr;
        _crop = crop;
        _zoneClassifier = zoneClassifier;
        _zoneScorer = zoneScorer;
        _symbolDetector = symbolDetector;
        _mapper = mapper;
        _scoring = scoring.Value;
        _logger = logger;
    }

    public async Task<Results<Ok<ScanResponse>, BadRequest<string>>> ScanAsync(
        HttpContext httpContext,
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
        await using (var ms = new MemoryStream(capacity: (int) image.Length))
        {
            await image.CopyToAsync(ms, ct);
            imageBytes = ms.ToArray();
        }

        var inputMediaType = string.IsNullOrEmpty(image.ContentType) ? "image/jpeg" : image.ContentType;
        var scanId = Guid.NewGuid();
        var scannedAt = DateTimeOffset.UtcNow;

        httpContext.TryGetOwnerSub(out var ownerSub);

        // Upload the original (pre-crop) image so a future, smarter extractor can
        // re-process the user's actual capture rather than our cropped derivative.
        // Best-effort: a MinIO failure must not break scanning.
        var imageObjectKey = !string.IsNullOrEmpty(ownerSub)
            ? BuildScanObjectKey(ownerSub, scannedAt, scanId, inputMediaType)
            : null;
        var uploadTask = imageObjectKey is null
            ? Task.FromResult(false)
            : this.UploadOriginalAsync(imageObjectKey, imageBytes, inputMediaType, ct);

        CardCropResult preprocessed;
        try
        {
            preprocessed = await _crop.PreprocessAsync(imageBytes, inputMediaType, ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Card crop preprocessing failed; continuing with original image");
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
        var symbolTask = preprocessed.Cropped
            ? _symbolDetector.DetectAsync(preprocessed.Bytes, preprocessed.MediaType, ct)
            : Task.FromResult<SetSymbolMatch?>(null);

        await Task.WhenAll(pHashTask, ocrTask, symbolTask, uploadTask);
        var (imageHash, pHashLatencyMs, pHashHits) = pHashTask.Result;
        var (regions, ocrLatencyMs) = ocrTask.Result;
        var symbolMatch = symbolTask.Result;
        var imageUploaded = uploadTask.Result;

        var zones = preprocessed.Width > 0 && preprocessed.Height > 0
            ? _zoneClassifier.Classify(regions, preprocessed.Width, preprocessed.Height, preprocessed.Cropped)
            : CardZones.Empty;

        var scoringResult = await _zoneScorer.ScoreAsync(zones, symbolMatch, ct);

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

        // Apply per-printing set-type weight as a multiplier so "real" sets
        // (expansion/core/masters) outrank funny/memorabilia near-ties on the same
        // OracleId. Done before the final sort so a strongly-weighted printing can
        // overtake a weakly-weighted one inside the top-N cut.
        var weights = await this.LoadSetTypeWeightsAsync(byPrinting.Keys, ct);
        foreach (var row in byPrinting.Values)
        {
            if (weights.TryGetValue(row.PrintingId, out var info))
            {
                row.SetCode = info.SetCode;
                row.SetType = info.SetType;
                row.SetTypeWeight = info.Weight;
            }

            row.FinalScore = Math.Clamp(row.FinalScore * row.SetTypeWeight, 0.0, 1.0);
        }

        var top = byPrinting.Values
            .OrderByDescending(r => r.FinalScore)
            .Take(FinalTopN)
            .ToList();

        var (ranked, hydratedRows) = await this.HydrateCandidatesAsync(top, ct);
        var confidence = this.ClassifyConfidence(ranked, hydratedRows);

        var response = new ScanResponse
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
                SetSymbol = symbolMatch is null ? null : new ScanSetSymbol
                {
                    SetCode = symbolMatch.SetCode,
                    HammingDistance = symbolMatch.HammingDistance,
                    Score = symbolMatch.Score,
                },
                ImagePHash = imageHash,
                Cropped = preprocessed.Cropped,
                CropConfidence = preprocessed.CropConfidence,
                CropRotated = preprocessed.Rotated,
                CroppedWidth = preprocessed.Width,
                CroppedHeight = preprocessed.Height,
                OcrRegionCount = regions.Regions.Count,
                PHashCandidateCount = pHashHits.Count,
                OcrCandidateCount = scoringResult.ByPrinting.Count,
                OcrLatencyMs = ocrLatencyMs,
                PHashLatencyMs = pHashLatencyMs,
            },
        };

        if (!string.IsNullOrEmpty(ownerSub))
        {
            await this.PersistScanLogAsync(
                scanId,
                ownerSub,
                scannedAt,
                imageUploaded ? imageObjectKey : null,
                inputMediaType,
                imageBytes.Length,
                imageHash,
                preprocessed,
                zones,
                symbolMatch,
                pHashLatencyMs,
                ocrLatencyMs,
                confidence,
                hydratedRows,
                ct);
        }

        return TypedResults.Ok(response);
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
        var printings = await _db.CardPrintings
            .AsNoTracking()
            .Where(p => topIds.Contains(p.Id))
            .ToListAsync(ct);
        var printingsById = printings.ToDictionary(p => p.Id, StringComparer.Ordinal);

        var setCodes = printings.Select(p => p.SetCode).Distinct().ToList();
        var setNames = await _db.Sets
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
            var printingResponse = await _mapper.MapAsync(printing, setName, ct);

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
                SetTypeWeight = row.SetTypeWeight,
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
            if (!_pHashIndex.IsLoaded)
            {
                return ((long?) null, 0, (IReadOnlyList<PHashIndex.PHashHit>) Array.Empty<PHashIndex.PHashHit>());
            }

            var sw = Stopwatch.StartNew();
            using var stream = new MemoryStream(imageBytes);
            long hash;
            try
            {
                hash = _pHash.Compute(stream);
            }
            catch (Exception ex)
            {
                sw.Stop();
                _logger.LogWarning(ex, "pHash compute failed; falling back to OCR-only candidates");
                return ((long?) null, (int) sw.ElapsedMilliseconds, (IReadOnlyList<PHashIndex.PHashHit>) Array.Empty<PHashIndex.PHashHit>());
            }

            var hits = _pHashIndex.Search(hash, PHashMaxHamming).Take(PHashTopK).ToList();
            sw.Stop();
            return ((long?) hash, (int) sw.ElapsedMilliseconds, (IReadOnlyList<PHashIndex.PHashHit>) hits);
        });
    }

    private async Task<(OcrRegions Regions, int LatencyMs)> RunOcrRegionsAsync(byte[] imageBytes, string mediaType, CancellationToken ct)
    {
        var sw = Stopwatch.StartNew();
        try
        {
            var regions = await _ocr.ReadRegionsAsync(imageBytes, mediaType, ct);
            sw.Stop();
            return (regions, (int) sw.ElapsedMilliseconds);
        }
        catch (Exception ex)
        {
            sw.Stop();
            _logger.LogWarning(ex, "OCR regions call failed; falling back to pHash-only candidates");
            return (OcrRegions.Empty, (int) sw.ElapsedMilliseconds);
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
            (true, true) => (_scoring.PHashWeight, _scoring.OcrWeight),
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

        if (best.CombinedScore >= _scoring.HighCombined && rows.Count > 0)
        {
            var contributing = rows[0].ZoneScores?.ContributingZoneCount(_scoring.HighZoneAgreementMinScore) ?? 0;
            if (contributing >= _scoring.HighZoneAgreementMinCount)
            {
                return RecognitionConfidence.High;
            }
        }

        if (best.CombinedScore >= _scoring.MediumCombined)
        {
            return RecognitionConfidence.Medium;
        }

        return RecognitionConfidence.Low;
    }

    private async Task<Dictionary<string, (string SetCode, string? SetType, double Weight)>> LoadSetTypeWeightsAsync(
        IReadOnlyCollection<string> printingIds,
        CancellationToken ct)
    {
        var result = new Dictionary<string, (string SetCode, string? SetType, double Weight)>(StringComparer.Ordinal);
        if (printingIds.Count == 0)
        {
            return result;
        }

        var ids = printingIds.ToList();
        var setCodeByPrinting = await _db.CardPrintings
            .AsNoTracking()
            .Where(p => ids.Contains(p.Id))
            .Select(p => new { p.Id, p.SetCode })
            .ToDictionaryAsync(x => x.Id, x => x.SetCode, StringComparer.Ordinal, ct);

        if (setCodeByPrinting.Count == 0)
        {
            return result;
        }

        var setCodes = setCodeByPrinting.Values.Distinct().ToList();
        var setTypeByCode = await _db.Sets
            .AsNoTracking()
            .Where(s => setCodes.Contains(s.Code))
            .Select(s => new { s.Code, s.SetType })
            .ToDictionaryAsync(x => x.Code, x => x.SetType, StringComparer.Ordinal, ct);

        var setTypes = setTypeByCode.Values.Distinct().ToList();
        var weightByType = setTypes.Count == 0
            ? new Dictionary<string, double>(StringComparer.Ordinal)
            : await _db.SetTypeWeights
                .AsNoTracking()
                .Where(w => setTypes.Contains(w.SetType))
                .ToDictionaryAsync(w => w.SetType, w => w.Weight, StringComparer.Ordinal, ct);

        foreach (var (id, setCode) in setCodeByPrinting)
        {
            string? setType = setTypeByCode.GetValueOrDefault(setCode);
            var weight = setType is not null && weightByType.TryGetValue(setType, out var w)
                ? w
                : DefaultSetTypeWeight;
            result[id] = (setCode, setType, weight);
        }

        return result;
    }

    private static string BuildScanObjectKey(string ownerSub, DateTimeOffset scannedAt, Guid scanId, string mediaType)
    {
        var ext = mediaType switch
        {
            "image/png" => "png",
            "image/webp" => "webp",
            _ => "jpg",
        };
        return $"scans/{ownerSub}/{scannedAt:yyyy}/{scannedAt:MM}/{scanId:N}.{ext}";
    }

    private async Task<bool> UploadOriginalAsync(string objectKey, byte[] bytes, string mediaType, CancellationToken ct)
    {
        try
        {
            using var ms = new MemoryStream(bytes, writable: false);
            await _images.PutAsync(objectKey, ms, mediaType, ct);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to upload scan image to object store at key {ObjectKey}", objectKey);
            return false;
        }
    }

    private async Task PersistScanLogAsync(
        Guid scanId,
        string ownerSub,
        DateTimeOffset scannedAt,
        string? imageObjectKey,
        string imageMediaType,
        int imageBytes,
        long? imagePHash,
        CardCropResult preprocessed,
        CardZones zones,
        SetSymbolMatch? symbolMatch,
        int pHashLatencyMs,
        int ocrLatencyMs,
        RecognitionConfidence confidence,
        IReadOnlyList<FinalRow> hydratedRows,
        CancellationToken ct)
    {
        try
        {
            var (supertype, type, subtype) = TypeLineParser.Parse(zones.TypeLine);
            var (power, toughness) = SplitPowerToughness(zones.PowerToughness);

            var doc = new ScanLogDocument
            {
                Id = scanId,
                OwnerSub = ownerSub,
                ScannedAt = scannedAt,
                ImageObjectKey = imageObjectKey,
                ImageMediaType = imageMediaType,
                ImageBytes = imageBytes,
                ImagePHash = imagePHash,
                Confidence = confidence,
                PHashLatencyMs = pHashLatencyMs,
                OcrLatencyMs = ocrLatencyMs,
                Cropped = preprocessed.Cropped,
                CropConfidence = preprocessed.CropConfidence,
                CroppedWidth = preprocessed.Width,
                CroppedHeight = preprocessed.Height,
                OcrName = NullIfEmpty(zones.Name),
                OcrTypeLine = NullIfEmpty(zones.TypeLine),
                OcrRulesText = NullIfEmpty(zones.RulesText),
                OcrPowerToughness = NullIfEmpty(zones.PowerToughness),
                OcrBottomMetadata = NullIfEmpty(zones.BottomMetadata),
                DetectedSetCode = symbolMatch?.SetCode,
                DetectedSetSymbolHamming = symbolMatch?.HammingDistance,
                ExtractedCardName = NullIfEmpty(zones.Name),
                ExtractedSupertype = supertype,
                ExtractedType = NullIfEmpty(type),
                ExtractedSubtype = subtype,
                ExtractedRulesText = NullIfEmpty(zones.RulesText),
                ExtractedPower = power,
                ExtractedToughness = toughness,
                ExtractedBottomLeftMetadata = NullIfEmpty(zones.BottomMetadata),
                Candidates = hydratedRows.Select(r => new ScanLogCandidate
                {
                    PrintingId = r.PrintingId,
                    SetCode = r.SetCode,
                    SetType = r.SetType,
                    SetTypeWeight = r.SetTypeWeight,
                    CombinedScore = r.FinalScore,
                    OcrAggregateScore = r.ZoneScores?.AggregateScore ?? 0.0,
                    NameScore = r.ZoneScores?.NameScore ?? 0.0,
                    TypeLineScore = r.ZoneScores?.TypeLineScore ?? 0.0,
                    RulesTextScore = r.ZoneScores?.RulesTextScore ?? 0.0,
                    PowerToughnessScore = r.ZoneScores?.PowerToughnessScore ?? 0.0,
                    BottomMetadataScore = r.ZoneScores?.BottomMetadataScore ?? 0.0,
                    HammingScore = r.HammingScore,
                    HammingDistance = r.HammingDistance,
                    MatchedByPHash = r.HammingDistance.HasValue,
                    MatchedByName = (r.ZoneScores?.NameScore ?? 0.0) > 0,
                }).ToList(),
            };

            _session.Store(doc);
            await _session.SaveChangesAsync(ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to persist scan log {ScanId}", scanId);
        }
    }

    private static (string? Power, string? Toughness) SplitPowerToughness(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return (null, null);
        }

        var trimmed = raw.Trim();
        var slash = trimmed.IndexOf('/');
        if (slash < 0)
        {
            return (null, null);
        }

        var power = trimmed[..slash].Trim();
        var toughness = trimmed[(slash + 1)..].Trim();
        return (
            string.IsNullOrEmpty(power) ? null : power,
            string.IsNullOrEmpty(toughness) ? null : toughness);
    }

    private static string? NullIfEmpty(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private sealed class FinalRow
    {
        public required string PrintingId { get; set; }

        public PrintingZoneScores? ZoneScores { get; set; }

        public double HammingScore { get; set; }

        public int? HammingDistance { get; set; }

        public double FinalScore { get; set; }

        public string SetCode { get; set; } = string.Empty;

        public string? SetType { get; set; }

        public double SetTypeWeight { get; set; } = DefaultSetTypeWeight;
    }
}
