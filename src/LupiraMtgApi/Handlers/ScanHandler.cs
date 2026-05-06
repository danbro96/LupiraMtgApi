using LupiraMtgApi.Data;
using LupiraMtgApi.Domain.ScanLog;
using LupiraMtgApi.Models;
using LupiraMtgApi.Services;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;
using System.Diagnostics;
using System.Diagnostics.Metrics;
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
    // OpenTelemetry source for the scan pipeline. Picked up by the global subscription
    // `AddSource("LupiraMtgApi.*")` in Program.cs. Each scan emits a root `scan` span
    // with child spans per phase; tags on the root span capture the recognition outcome
    // so OpenObserve can filter/aggregate by confidence, top set, rotation retry, etc.
    private static readonly ActivitySource ScanActivity = new("LupiraMtgApi.Scans");

    // Metrics — spans answer "what happened on this one scan", metrics answer "what's
    // the p95 OCR latency this week" and "how many Low-confidence scans today". Tag
    // domains are intentionally bounded (confidence, has_phash, has_ocr) — anything
    // unbounded (printing id, set code, owner) belongs on spans, not metric tags.
    private static readonly Meter ScanMeter = new("LupiraMtgApi.Scans");
    private static readonly Histogram<double> ScanDurationHist = ScanMeter.CreateHistogram<double>("scan.duration_ms", unit: "ms", description: "End-to-end scan latency");
    private static readonly Histogram<double> OcrDurationHist = ScanMeter.CreateHistogram<double>("scan.ocr.duration_ms", unit: "ms", description: "OCR latency including any rotation-retry pass");
    private static readonly Histogram<double> PhashDurationHist = ScanMeter.CreateHistogram<double>("scan.phash.duration_ms", unit: "ms", description: "pHash compute + index search latency");
    private static readonly Histogram<int> OcrRegionHist = ScanMeter.CreateHistogram<int>("scan.ocr.region_count", description: "Number of OCR regions returned by Florence");
    private static readonly Histogram<int> PhashCandidateHist = ScanMeter.CreateHistogram<int>("scan.phash.candidate_count", description: "BK-tree candidates within the hamming cutoff (merged art + full-card)");
    private static readonly Histogram<int> FullPhashCandidateHist = ScanMeter.CreateHistogram<int>("scan.phash.full.candidate_count", description: "Full-card BK-tree hits within the hamming cutoff");
    private static readonly Counter<long> WinningSourceCounter = ScanMeter.CreateCounter<long>("scan.phash.winning_source.total", description: "pHash signal carrying the top match: art / full / both / neither");
    private static readonly Histogram<int> ZoneCoverageHist = ScanMeter.CreateHistogram<int>("scan.zone.coverage", description: "Number of zones with meaningful content (0..5)");
    private static readonly Histogram<double> TopCombinedHist = ScanMeter.CreateHistogram<double>("scan.top.combined_score", description: "Final combined score of the top candidate");
    private static readonly Counter<long> ConfidenceCounter = ScanMeter.CreateCounter<long>("scan.confidence.total", description: "Scans by confidence outcome");
    private static readonly Counter<long> RotationRetryCounter = ScanMeter.CreateCounter<long>("scan.rotation.retried.total", description: "Scans that ran the alt-rotation OCR pass");
    private static readonly Counter<long> CropFailureCounter = ScanMeter.CreateCounter<long>("scan.crop.failures.total", description: "Crop preprocessor exceptions");
    private static readonly Counter<long> OcrFailureCounter = ScanMeter.CreateCounter<long>("scan.ocr.failures.total", description: "Florence OCR-call exceptions");
    private static readonly Counter<long> PhashFailureCounter = ScanMeter.CreateCounter<long>("scan.phash.failures.total", description: "pHash compute exceptions");
    private static readonly Counter<long> UploadFailureCounter = ScanMeter.CreateCounter<long>("scan.upload.failures.total", description: "MinIO upload exceptions on the scan path");

    private const int MaxImageBytes = 4 * 1024 * 1024;
    private const int PHashTopK = 10;
    private const int FinalTopN = 5;
    private const int PHashMaxHamming = 12;

    // If the cropper rotated to portrait but the first pass populates fewer than this many
    // zones, try the other 90° rotation (180° flip of the current bytes). 3 = need at
    // least Name + Type + one more before we trust the first pass; basic lands and vanilla
    // creatures naturally hit 3+ when correctly oriented.
    private const int RotationRetryCoverageThreshold = 3;

    // Skip the retry entirely when the first pass already looks strong. Two ways: very
    // high coverage (4-5 zones) OR borderline coverage (3) with multi-zone agreement on
    // the top candidate. Keeps the retry from costing ~1s on confident scans where the
    // alt rotation would not win anyway.
    private const int RotationRetryHighCoverageSkipThreshold = 4;
    private const double RotationRetryStrongZoneScoreThreshold = 0.7;
    private const int RotationRetryStrongZoneMinCount = 3;

    // Used when a printing's set has no matching set_type_weights row (defensive
    // against new Scryfall set_types we haven't seeded). Neutral midpoint so an
    // unknown set still ranks plausibly.
    private const double DefaultSetTypeWeight = 0.5;

    private readonly LupiraMtgDbContext _db;
    private readonly Marten.IDocumentSession _session;
    private readonly IImageStore _images;
    private readonly PHashIndex _pHashIndex;
    private readonly FullCardPHashIndex _fullCardPHashIndex;
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
        FullCardPHashIndex fullCardPHashIndex,
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
        _fullCardPHashIndex = fullCardPHashIndex;
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

        var hasOwner = httpContext.TryGetOwnerId(out var ownerId);

        var scanStopwatch = Stopwatch.StartNew();
        using var rootSpan = ScanActivity.StartActivity("scan");
        rootSpan?.SetTag("scan.id", scanId);
        rootSpan?.SetTag("scan.owner_id", hasOwner ? ownerId.ToString() : "anon");
        rootSpan?.SetTag("scan.image_bytes", imageBytes.Length);
        rootSpan?.SetTag("scan.media_type", inputMediaType);

        // Upload the original (pre-crop) image so a future, smarter extractor can
        // re-process the user's actual capture rather than our cropped derivative.
        // Best-effort: a MinIO failure must not break scanning.
        var imageObjectKey = hasOwner
            ? BuildScanObjectKey(ownerId, scannedAt, scanId, inputMediaType)
            : null;
        var uploadTask = imageObjectKey is null
            ? Task.FromResult(false)
            : this.UploadOriginalAsync(imageObjectKey, imageBytes, inputMediaType, scanId, ct);

        CardCropResult preprocessed;
        using (var cropSpan = ScanActivity.StartActivity("crop.preprocess"))
        {
            try
            {
                preprocessed = await _crop.PreprocessAsync(imageBytes, inputMediaType, ct);
                cropSpan?.SetTag("crop.success", true);
                cropSpan?.SetTag("crop.cropped", preprocessed.IsCropped);
                cropSpan?.SetTag("crop.confidence", preprocessed.CropConfidence);
                cropSpan?.SetTag("crop.rotated", preprocessed.Rotated);
                cropSpan?.SetTag("crop.width", preprocessed.Width);
                cropSpan?.SetTag("crop.height", preprocessed.Height);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Card crop preprocessing failed for scan {ScanId}; continuing with original image", scanId);
                cropSpan?.SetTag("crop.success", false);
                cropSpan?.SetTag("error.type", ex.GetType().Name);
                CropFailureCounter.Add(1, new KeyValuePair<string, object?>("error.type", ex.GetType().Name));
                var (fallbackW, fallbackH) = ProbeImageSize(imageBytes);
                preprocessed = new CardCropResult
                {
                    Bytes = imageBytes,
                    MediaType = inputMediaType,
                    IsCropped = false,
                    CropConfidence = 0.0,
                    Width = fallbackW,
                    Height = fallbackH,
                };
            }
        }

        // First-pass pHash tries both rotations when the cropper had to rotate — the CW
        // default may be wrong, and pHash has no other recovery path (OCR has the
        // rotation retry below to cover its own case). Cost: one extra ~110ms hash
        // compute on rotated scans, and pHash actually starts producing hits where it
        // was silently returning 0 before.
        var pHashTask = this.RunPHashAsync(preprocessed.Bytes, scanId, tryAltRotation: preprocessed.Rotated);
        var ocrTask = this.RunOcrRegionsAsync(preprocessed.Bytes, preprocessed.MediaType, scanId, ct);
        var symbolTask = preprocessed.IsCropped
            ? this.RunSymbolDetectAsync(preprocessed.Bytes, preprocessed.MediaType, ct)
            : Task.FromResult<SetSymbolMatch?>(null);

        await Task.WhenAll(pHashTask, ocrTask, symbolTask, uploadTask);
        var (imageHash, pHashLatencyMs, pHashHits) = pHashTask.Result;
        var (regions, ocrLatencyMs) = ocrTask.Result;
        var symbolMatch = symbolTask.Result;
        var imageUploaded = uploadTask.Result;

        CardZones zones;
        using (var zoneSpan = ScanActivity.StartActivity("zone.classify"))
        {
            zones = preprocessed.Width > 0 && preprocessed.Height > 0
                ? _zoneClassifier.Classify(regions, preprocessed.Width, preprocessed.Height, preprocessed.IsCropped)
                : CardZones.Empty;
            zoneSpan?.SetTag("zone.coverage_score", ZoneCoverageScore(zones));
        }

        // First-pass scoring runs before the retry decision so we can use multi-zone
        // agreement as a "first pass is confident" signal. Production traces show the
        // retry rarely wins from a confident first pass — running it costs ~1s for no
        // gain. Re-scoring on retry-win is fine: the second call hits warm caches.
        CardZoneScoringResult scoringResult;
        using (var scoreSpan = ScanActivity.StartActivity("zone.score"))
        {
            scoringResult = await _zoneScorer.ScoreAsync(zones, symbolMatch, ct);
            scoreSpan?.SetTag("zone.candidate_count", scoringResult.ByPrinting.Count);
            scoreSpan?.SetTag("zone.weights_total", scoringResult.Weights.TotalPresent);
        }

        // Two-pass OCR: when CardCropService had to rotate (landscape bbox → portrait),
        // it picked clockwise by default. If the first pass produced sparse zones the card
        // might be upside-down — flip 180° and retry. We pick whichever pass populated
        // more zones. pHash and symbol detection ride along so all three signals reflect
        // the same orientation.
        var rotationRetried = false;
        if (preprocessed.Rotated && !IsFirstPassConfident(zones, scoringResult, rootSpan))
        {
            using var retrySpan = ScanActivity.StartActivity("rotation.retry");
            retrySpan?.SetTag("rotation.first_pass_score", ZoneCoverageScore(zones));
            try
            {
                var altBytes = await Rotate180Async(preprocessed.Bytes, ct);
                var altPHashTask = this.RunPHashAsync(altBytes, scanId);
                var altOcrTask = this.RunOcrRegionsAsync(altBytes, preprocessed.MediaType, scanId, ct);
                var altSymbolTask = preprocessed.IsCropped
                    ? this.RunSymbolDetectAsync(altBytes, preprocessed.MediaType, ct)
                    : Task.FromResult<SetSymbolMatch?>(null);

                await Task.WhenAll(altPHashTask, altOcrTask, altSymbolTask);
                var (altImageHash, altPHashLatencyMs, altPHashHits) = altPHashTask.Result;
                var (altRegions, altOcrLatencyMs) = altOcrTask.Result;
                var altSymbolMatch = altSymbolTask.Result;

                var altZones = preprocessed.Width > 0 && preprocessed.Height > 0
                    ? _zoneClassifier.Classify(altRegions, preprocessed.Width, preprocessed.Height, preprocessed.IsCropped)
                    : CardZones.Empty;

                var altCoverage = ZoneCoverageScore(altZones);
                retrySpan?.SetTag("rotation.alt_pass_score", altCoverage);

                if (altCoverage > ZoneCoverageScore(zones))
                {
                    rotationRetried = true;
                    zones = altZones;
                    regions = altRegions;
                    symbolMatch = altSymbolMatch;
                    imageHash = altImageHash;
                    pHashHits = altPHashHits;
                    preprocessed = new CardCropResult
                    {
                        Bytes = altBytes,
                        MediaType = preprocessed.MediaType,
                        IsCropped = preprocessed.IsCropped,
                        CropConfidence = preprocessed.CropConfidence,
                        Width = preprocessed.Width,
                        Height = preprocessed.Height,
                        Rotated = preprocessed.Rotated,
                    };

                    // Re-score on the winning rotation so downstream candidates and
                    // confidence reflect the alt-pass zones, not the first pass.
                    using var rescoreSpan = ScanActivity.StartActivity("zone.score.rescore");
                    scoringResult = await _zoneScorer.ScoreAsync(zones, symbolMatch, ct);
                    rescoreSpan?.SetTag("zone.candidate_count", scoringResult.ByPrinting.Count);
                }

                retrySpan?.SetTag("rotation.alt_won", rotationRetried);

                // Add both passes' latencies regardless of which pass won, so telemetry
                // reflects the true time cost of the retry.
                ocrLatencyMs += altOcrLatencyMs;
                pHashLatencyMs += altPHashLatencyMs;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Rotation retry failed for scan {ScanId}; keeping first-pass results", scanId);
                retrySpan?.SetTag("error.type", ex.GetType().Name);
            }
        }
        else if (preprocessed.Rotated)
        {
            // First pass was confident — skipped the retry. Tag for telemetry.
            var skipReason = ZoneCoverageScore(zones) >= RotationRetryHighCoverageSkipThreshold
                ? "high_coverage"
                : "strong_zone_agreement";
            rootSpan?.SetTag("rotation.skipped_reason", skipReason);
        }

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
        Dictionary<string, (string SetCode, string? SetType, double Weight)> weights;
        using (var weightsSpan = ScanActivity.StartActivity("set_type_weights.load"))
        {
            weights = await this.LoadSetTypeWeightsAsync(byPrinting.Keys, ct);
            weightsSpan?.SetTag("set_type_weights.count", weights.Count);
        }

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

        List<CardCandidateResponse> ranked;
        List<FinalRow> hydratedRows;
        using (var hydrateSpan = ScanActivity.StartActivity("hydrate"))
        {
            (ranked, hydratedRows) = await this.HydrateCandidatesAsync(top, ct);
            hydrateSpan?.SetTag("hydrate.count", ranked.Count);
        }

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
                IsCropped = preprocessed.IsCropped,
                CropConfidence = preprocessed.CropConfidence,
                CropRotated = preprocessed.Rotated,
                RotationRetried = rotationRetried,
                CroppedWidth = preprocessed.Width,
                CroppedHeight = preprocessed.Height,
                OcrRegionCount = regions.Regions.Count,
                PHashCandidateCount = pHashHits.Count,
                OcrCandidateCount = scoringResult.ByPrinting.Count,
                OcrLatencyMs = ocrLatencyMs,
                PHashLatencyMs = pHashLatencyMs,
            },
        };

        // Outcome tags on the root span. Filterable in OpenObserve — e.g.
        //   service.name=lupira-mtg-api AND scan.confidence=Low AND scan.phash.candidate_count=0
        // shows the "card never matches" cohort that motivates the next round of tuning.
        var topCandidate = ranked.FirstOrDefault();
        var topRow = hydratedRows.FirstOrDefault();
        rootSpan?.SetTag("scan.confidence", confidence.ToString());
        rootSpan?.SetTag("scan.crop.cropped", preprocessed.IsCropped);
        rootSpan?.SetTag("scan.crop.confidence", preprocessed.CropConfidence);
        rootSpan?.SetTag("scan.crop.rotated", preprocessed.Rotated);
        rootSpan?.SetTag("scan.rotation.retried", rotationRetried);
        rootSpan?.SetTag("scan.ocr.region_count", regions.Regions.Count);
        rootSpan?.SetTag("scan.ocr.candidate_count", scoringResult.ByPrinting.Count);
        rootSpan?.SetTag("scan.phash.candidate_count", pHashHits.Count);
        rootSpan?.SetTag("scan.phash.has_index", _pHashIndex.IsLoaded);
        rootSpan?.SetTag("scan.symbol.matched", symbolMatch is not null);
        rootSpan?.SetTag("scan.symbol.set_code", symbolMatch?.SetCode);
        rootSpan?.SetTag("scan.symbol.hamming", symbolMatch?.HammingDistance);
        if (topCandidate is not null)
        {
            rootSpan?.SetTag("scan.top.printing_id", topCandidate.Printing.Id);
            rootSpan?.SetTag("scan.top.set_code", topCandidate.Printing.SetCode);
            rootSpan?.SetTag("scan.top.combined", topCandidate.CombinedScore);
            rootSpan?.SetTag("scan.top.ocr_aggregate", topCandidate.OcrAggregateScore);
            rootSpan?.SetTag("scan.top.phash", topCandidate.HammingScore);
            rootSpan?.SetTag("scan.top.set_type_weight", topCandidate.SetTypeWeight);
            rootSpan?.SetTag("scan.top.set_type", topRow?.SetType);
        }

        // Greppable one-liner summary. Trace_id is auto-correlated by the OTel logger,
        // so this line is the bridge between log search ("show me Low-confidence scans")
        // and trace deep-dive (jump to the flame graph for that scan).
        _logger.LogInformation(
            "Scan {ScanId} -> {Confidence} top={TopName} set={TopSetCode} combined={Combined:F3} ocr={OcrAgg:F3} pHash={PHashScore:F3} cropRotated={Rotated} retried={Retried} ocrRegions={Regions} pHashCandidates={PHashCandidates}",
            scanId,
            confidence,
            topCandidate?.Printing.Name ?? "(none)",
            topCandidate?.Printing.SetCode ?? "-",
            topCandidate?.CombinedScore ?? 0.0,
            topCandidate?.OcrAggregateScore ?? 0.0,
            topCandidate?.HammingScore ?? 0.0,
            preprocessed.Rotated,
            rotationRetried,
            regions.Regions.Count,
            pHashHits.Count);

        // Metrics — histograms get tagged with bounded-domain dimensions only. The
        // scan.confidence counter splits by outcome so dashboards can chart the daily
        // High/Medium/Low ratio without scraping spans.
        scanStopwatch.Stop();
        var confidenceTag = new KeyValuePair<string, object?>("confidence", confidence.ToString());
        var rotatedTag = new KeyValuePair<string, object?>("crop.rotated", preprocessed.Rotated);
        var retriedTag = new KeyValuePair<string, object?>("rotation.retried", rotationRetried);
        ScanDurationHist.Record(scanStopwatch.Elapsed.TotalMilliseconds, confidenceTag, rotatedTag, retriedTag);
        OcrDurationHist.Record(ocrLatencyMs, retriedTag);
        PhashDurationHist.Record(pHashLatencyMs, retriedTag);
        OcrRegionHist.Record(regions.Regions.Count);
        PhashCandidateHist.Record(pHashHits.Count);
        ZoneCoverageHist.Record(ZoneCoverageScore(zones));
        if (topCandidate is not null)
        {
            TopCombinedHist.Record(topCandidate.CombinedScore, confidenceTag);
        }

        ConfidenceCounter.Add(1, confidenceTag);
        if (rotationRetried)
        {
            RotationRetryCounter.Add(1);
        }

        // Verbose detail for investigations. Default off in production; bump the logger
        // level to Debug for `LupiraMtgApi.Handlers.ScanHandler` to enable.
        if (_logger.IsEnabled(LogLevel.Debug))
        {
            _logger.LogDebug(
                "Scan {ScanId} zones name={Name} type={Type} pt={PT} meta={Meta} rules.len={RulesLen}",
                scanId,
                zones.Name,
                zones.TypeLine,
                zones.PowerToughness,
                zones.BottomMetadata,
                zones.RulesText?.Length ?? 0);
        }

        if (hasOwner)
        {
            using var persistSpan = ScanActivity.StartActivity("persist_log");
            await this.PersistScanLogAsync(
                scanId,
                ownerId,
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

    // Modern-frame art rectangle, in card-relative coords. Aspect ~0.90×0.495 normalized
    // by card aspect 0.72 ≈ 1.45, close to Scryfall's art_crop (~1.37). The BK-tree was
    // built on hashes of Scryfall's art_crop bytes during sync, so the scan-side hash
    // must come from the same rectangle of the photo — hashing the full card produces
    // a signature dominated by frame + text, which has near-zero correlation with
    // art-only hashes. Rectangle is intentionally a bit looser than the strict art
    // crop so small misalignment after rotation doesn't push the rectangle off the art.
    private const double ArtCropYMin = 0.08;
    private const double ArtCropYMax = 0.575;
    private const double ArtCropXMin = 0.05;
    private const double ArtCropXMax = 0.95;

    private Task<(long? Hash, int LatencyMs, IReadOnlyList<PHashIndex.PHashHit> Hits)> RunPHashAsync(byte[] imageBytes, Guid scanId, bool tryAltRotation = false)
    {
        // Capture the parent activity context so the Task.Run continuation parents its
        // span under the root scan span, not under the thread-pool worker's empty
        // context. Without this, the phash span would orphan from the trace tree.
        var parent = Activity.Current;
        return Task.Run(() =>
        {
            using var span = ScanActivity.StartActivity("phash.compute", ActivityKind.Internal, parent?.Context ?? default);
            var artLoaded = _pHashIndex.IsLoaded;
            var fullLoaded = _fullCardPHashIndex.IsLoaded;
            if (!artLoaded && !fullLoaded)
            {
                span?.SetTag("phash.art_index_loaded", false);
                span?.SetTag("phash.full_index_loaded", false);
                return ((long?) null, 0, (IReadOnlyList<PHashIndex.PHashHit>) Array.Empty<PHashIndex.PHashHit>());
            }

            span?.SetTag("phash.art_index_loaded", artLoaded);
            span?.SetTag("phash.full_index_loaded", fullLoaded);
            span?.SetTag("phash.art_index_size", _pHashIndex.Count);
            span?.SetTag("phash.full_index_size", _fullCardPHashIndex.Count);

            var fullCardHamming = _scoring.FullCardPHashMaxHamming;
            var sw = Stopwatch.StartNew();
            try
            {
                using var stream = new MemoryStream(imageBytes);
                using var fullImg = Image.Load<Rgba32>(stream);

                // Full-card pHash uses the WHOLE cropped image — no rectangle extraction.
                // Hash the full image first while we still have it intact, then re-clone
                // the same source bytes for the art-crop branch so the two paths don't
                // have to re-decode.
                var (fullHash, fullHits, fullBest, fullRotation) = ComputeAndSearch(
                    fullImg,
                    tryAltRotation,
                    h => _fullCardPHashIndex.Search(h, fullCardHamming).Take(PHashTopK).ToList());

                // Art-only pHash. Clone from the original buffer so we keep `fullImg`
                // available as the post-rotation winning source if needed.
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

                var (artHash, artHits, artBest, artRotation) = ComputeAndSearch(
                    artImg,
                    tryAltRotation,
                    hh => _pHashIndex.Search(hh, PHashMaxHamming).Take(PHashTopK).ToList());

                // Merge: per-printing minimum hamming across both indexes. A printing
                // appearing in both gets the smaller of the two distances; this is what
                // makes the two signals compensate for each other (foil cards lose
                // full-card quality but keep art; misaligned art rectangle loses art
                // quality but keeps full-card).
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
                    .OrderBy(h => h.Distance)
                    .Take(PHashTopK)
                    .ToList();

                // Determine which signal carried the top match for telemetry. "both"
                // means the same printing appeared in both indexes (independent
                // confirmation); "art" / "full" means only one side had it.
                var winningSource = "neither";
                if (hits.Count > 0)
                {
                    var topId = hits[0].PrintingId;
                    var inArt = artHits.Any(h => h.PrintingId == topId);
                    var inFull = fullHits.Any(h => h.PrintingId == topId);
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

                FullPhashCandidateHist.Record(fullHits.Count);
                WinningSourceCounter.Add(1, new KeyValuePair<string, object?>("source", winningSource));

                sw.Stop();
                // Surface the art hash on the response (legacy field) — full hash is in
                // span tags only. Order doesn't really matter; art is the one users see.
                return ((long?) artHash, (int) sw.ElapsedMilliseconds, (IReadOnlyList<PHashIndex.PHashHit>) hits);
            }
            catch (Exception ex)
            {
                sw.Stop();
                _logger.LogWarning(ex, "pHash compute failed for scan {ScanId}; falling back to OCR-only candidates", scanId);
                span?.SetTag("error.type", ex.GetType().Name);
                PhashFailureCounter.Add(1, new KeyValuePair<string, object?>("error.type", ex.GetType().Name));
                return ((long?) null, (int) sw.ElapsedMilliseconds, (IReadOnlyList<PHashIndex.PHashHit>) Array.Empty<PHashIndex.PHashHit>());
            }
        });
    }

    // Helper: hashes an image, optionally also its 180° rotation, and returns whichever
    // side produced the lower best-hamming hit. Used by both the art-pHash and full-card
    // pHash branches in RunPHashAsync.
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

    private async Task<(OcrRegions Regions, int LatencyMs)> RunOcrRegionsAsync(byte[] imageBytes, string mediaType, Guid scanId, CancellationToken ct)
    {
        using var span = ScanActivity.StartActivity("ocr.regions");
        span?.SetTag("ocr.image_bytes", imageBytes.Length);

        var sw = Stopwatch.StartNew();
        try
        {
            var regions = await _ocr.ReadRegionsAsync(imageBytes, mediaType, ct);
            sw.Stop();
            span?.SetTag("ocr.region_count", regions.Regions.Count);
            return (regions, (int) sw.ElapsedMilliseconds);
        }
        catch (Exception ex)
        {
            sw.Stop();
            _logger.LogWarning(ex, "OCR regions call failed for scan {ScanId}; falling back to pHash-only candidates", scanId);
            span?.SetTag("error.type", ex.GetType().Name);
            OcrFailureCounter.Add(1, new KeyValuePair<string, object?>("error.type", ex.GetType().Name));
            return (OcrRegions.Empty, (int) sw.ElapsedMilliseconds);
        }
    }

    private async Task<SetSymbolMatch?> RunSymbolDetectAsync(byte[] bytes, string mediaType, CancellationToken ct)
    {
        using var span = ScanActivity.StartActivity("symbol.detect");
        var match = await _symbolDetector.DetectAsync(bytes, mediaType, ct);
        span?.SetTag("symbol.matched", match is not null);
        if (match is not null)
        {
            span?.SetTag("symbol.set_code", match.SetCode);
            span?.SetTag("symbol.hamming", match.HammingDistance);
            span?.SetTag("symbol.score", match.Score);
        }

        return match;
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

    private static async Task<byte[]> Rotate180Async(byte[] bytes, CancellationToken ct)
    {
        await using var input = new MemoryStream(bytes, writable: false);
        using var img = await Image.LoadAsync<Rgba32>(input, ct);
        img.Mutate(c => c.Rotate(RotateMode.Rotate180));

        await using var output = new MemoryStream();
        await img.SaveAsJpegAsync(output, ct);
        return output.ToArray();
    }

    // Decides whether the first OCR pass is strong enough to skip the 180° rotation
    // retry. Two acceptance paths:
    //   1. High coverage (4-5 zones populated) → trust regardless of agreement quality.
    //   2. Borderline coverage (3) AND the top OCR candidate has multi-zone agreement
    //      at ≥ 0.7 in 3+ zones → trust because both classifier and matcher concur.
    // Below the existing RotationRetryCoverageThreshold (3), retry runs as before.
    private static bool IsFirstPassConfident(CardZones zones, CardZoneScoringResult scoring, Activity? rootSpan)
    {
        var coverage = ZoneCoverageScore(zones);
        if (coverage >= RotationRetryHighCoverageSkipThreshold)
        {
            rootSpan?.SetTag("rotation.first_pass_confidence", "high_coverage");
            return true;
        }

        if (coverage < RotationRetryCoverageThreshold)
        {
            return false;
        }

        var topRow = scoring.ByPrinting.Values
            .OrderByDescending(r => r.AggregateScore)
            .FirstOrDefault();
        if (topRow is not null
            && topRow.ContributingZoneCount(RotationRetryStrongZoneScoreThreshold) >= RotationRetryStrongZoneMinCount)
        {
            rootSpan?.SetTag("rotation.first_pass_confidence", "strong_zone_agreement");
            return true;
        }

        return false;
    }

    // Counts zones that have meaningful content. The Name 3-char floor and RulesText
    // 12-char floor mirror the cutoffs used elsewhere — short strings on those zones
    // are usually OCR noise rather than real card text.
    private static int ZoneCoverageScore(CardZones zones)
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

    private static string BuildScanObjectKey(Guid ownerId, DateTimeOffset scannedAt, Guid scanId, string mediaType)
    {
        var ext = mediaType switch
        {
            "image/png" => "png",
            "image/webp" => "webp",
            _ => "jpg",
        };
        return $"scans/{ownerId:N}/{scannedAt:yyyy}/{scannedAt:MM}/{scanId:N}.{ext}";
    }

    private async Task<bool> UploadOriginalAsync(string objectKey, byte[] bytes, string mediaType, Guid scanId, CancellationToken ct)
    {
        using var span = ScanActivity.StartActivity("upload.original");
        span?.SetTag("upload.object_key", objectKey);
        span?.SetTag("upload.bytes", bytes.Length);
        try
        {
            using var ms = new MemoryStream(bytes, writable: false);
            await _images.PutAsync(objectKey, ms, mediaType, ct);
            span?.SetTag("upload.success", true);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to upload scan image for scan {ScanId} to object store at key {ObjectKey}", scanId, objectKey);
            span?.SetTag("upload.success", false);
            span?.SetTag("error.type", ex.GetType().Name);
            UploadFailureCounter.Add(1, new KeyValuePair<string, object?>("error.type", ex.GetType().Name));
            return false;
        }
    }

    private async Task PersistScanLogAsync(
        Guid scanId,
        Guid ownerId,
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
                OwnerId = ownerId,
                ScannedAt = scannedAt,
                ImageObjectKey = imageObjectKey,
                ImageMediaType = imageMediaType,
                ImageBytes = imageBytes,
                ImagePHash = imagePHash,
                Confidence = confidence,
                PHashLatencyMs = pHashLatencyMs,
                OcrLatencyMs = ocrLatencyMs,
                IsCropped = preprocessed.IsCropped,
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
