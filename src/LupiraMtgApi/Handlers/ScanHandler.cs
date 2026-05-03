using System.Diagnostics;
using LupiraMtgApi.Data;
using LupiraMtgApi.Data.Entities;
using LupiraMtgApi.Models;
using LupiraMtgApi.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace LupiraMtgApi.Handlers;

public sealed class ScanHandler
{
    private const int MaxImageBytes = 4 * 1024 * 1024;
    private const int PHashTopK = 10;
    private const int OcrTopK = 10;
    private const int FinalTopN = 5;

    // Confidence thresholds — config-bind once we have production data; for now these
    // mirror the values quoted in the architecture plan.
    private const double HighCombined = 0.85;
    private const double HighName = 0.75;
    private const double MediumCombined = 0.60;
    private const double MediumName = 0.50;
    private const double NameCutoff = 0.30;

    private const int PHashMaxHamming = 12;

    private readonly LupiraMtgDbContext db;
    private readonly PHashIndex pHashIndex;
    private readonly PHashService pHash;
    private readonly IOcrService ocr;
    private readonly CardPrintingMapper mapper;
    private readonly ILogger<ScanHandler> logger;

    public ScanHandler(
        LupiraMtgDbContext db,
        PHashIndex pHashIndex,
        PHashService pHash,
        IOcrService ocr,
        CardPrintingMapper mapper,
        ILogger<ScanHandler> logger)
    {
        this.db = db;
        this.pHashIndex = pHashIndex;
        this.pHash = pHash;
        this.ocr = ocr;
        this.mapper = mapper;
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

        var mediaType = string.IsNullOrEmpty(image.ContentType) ? "image/jpeg" : image.ContentType;

        var pHashTask = this.RunPHashAsync(imageBytes);
        var ocrTask = this.RunOcrAsync(imageBytes, mediaType, ct);

        await Task.WhenAll(pHashTask, ocrTask);
        var (imageHash, pHashLatencyMs, pHashHits) = pHashTask.Result;
        var (ocrText, ocrLatencyMs) = ocrTask.Result;

        var ocrCandidates = await this.LookupByOcrAsync(ocrText, ct);

        var combined = await this.CombineAndRankAsync(pHashHits, ocrCandidates, ct);

        var confidence = ClassifyConfidence(combined);

        var top = combined.Take(FinalTopN).ToList();

        return TypedResults.Ok(new ScanResponse
        {
            Confidence = confidence,
            Candidates = top,
            Debug = new ScanDebug
            {
                OcrText = string.IsNullOrWhiteSpace(ocrText) ? null : ocrText,
                ImagePHash = imageHash,
                PHashCandidateCount = pHashHits.Count,
                OcrCandidateCount = ocrCandidates.Count,
                OcrLatencyMs = ocrLatencyMs,
                PHashLatencyMs = pHashLatencyMs,
            },
        });
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

    private async Task<(string Text, int LatencyMs)> RunOcrAsync(byte[] imageBytes, string mediaType, CancellationToken ct)
    {
        var sw = Stopwatch.StartNew();
        try
        {
            var text = await this.ocr.ReadTextAsync(imageBytes, mediaType, ct);
            sw.Stop();
            return (text, (int)sw.ElapsedMilliseconds);
        }
        catch (Exception ex)
        {
            sw.Stop();
            this.logger.LogWarning(ex, "OCR call failed; falling back to pHash-only candidates");
            return (string.Empty, (int)sw.ElapsedMilliseconds);
        }
    }

    private async Task<List<(CardPrinting Printing, double NameScore)>> LookupByOcrAsync(
        string ocrText,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(ocrText))
        {
            return new List<(CardPrinting, double)>();
        }

        var trimmed = ocrText.Trim();

        var rows = await this.db.CardPrintings
            .AsNoTracking()
            .Select(p => new
            {
                Printing = p,
                Score = EF.Functions.TrigramsWordSimilarity(p.Name, trimmed),
            })
            .Where(x => x.Score > NameCutoff)
            .OrderByDescending(x => x.Score)
            .Take(OcrTopK)
            .ToListAsync(ct);

        return rows.Select(r => (r.Printing, (double)r.Score)).ToList();
    }

    private async Task<List<CardCandidateResponse>> CombineAndRankAsync(
        IReadOnlyList<PHashIndex.PHashHit> pHashHits,
        List<(CardPrinting Printing, double NameScore)> ocrCandidates,
        CancellationToken ct)
    {
        var byId = new Dictionary<string, CombinedRow>();

        foreach (var hit in pHashHits)
        {
            var hammingScore = Math.Clamp(1.0 - (hit.Distance / 64.0), 0.0, 1.0);
            byId[hit.PrintingId] = new CombinedRow
            {
                PrintingId = hit.PrintingId,
                HammingDistance = hit.Distance,
                HammingScore = hammingScore,
                MatchedByPHash = true,
            };
        }

        foreach (var (printing, nameScore) in ocrCandidates)
        {
            if (!byId.TryGetValue(printing.Id, out var row))
            {
                row = new CombinedRow { PrintingId = printing.Id };
                byId[printing.Id] = row;
            }

            row.NameScore = nameScore;
            row.MatchedByName = true;
            row.PrintingEntity = printing;
        }

        // Hydrate any pHash-only printings (no OCR row to bring the entity along).
        var missing = byId.Values
            .Where(r => r.PrintingEntity is null)
            .Select(r => r.PrintingId)
            .ToList();
        if (missing.Count > 0)
        {
            var rows = await this.db.CardPrintings
                .AsNoTracking()
                .Where(p => missing.Contains(p.Id))
                .ToListAsync(ct);
            foreach (var p in rows)
            {
                if (byId.TryGetValue(p.Id, out var row))
                {
                    row.PrintingEntity = p;
                }
            }
        }

        var setCodes = byId.Values
            .Where(r => r.PrintingEntity is not null)
            .Select(r => r.PrintingEntity!.SetCode)
            .Distinct()
            .ToList();
        var setNames = await this.db.Sets
            .AsNoTracking()
            .Where(s => setCodes.Contains(s.Code))
            .ToDictionaryAsync(s => s.Code, s => s.Name, ct);

        var ranked = new List<CardCandidateResponse>(byId.Count);
        foreach (var row in byId.Values)
        {
            if (row.PrintingEntity is null)
            {
                continue;
            }

            var combined = (0.5 * row.HammingScore) + (0.5 * row.NameScore);
            var setName = setNames.GetValueOrDefault(row.PrintingEntity.SetCode, row.PrintingEntity.SetCode);
            var printingResponse = await this.mapper.MapAsync(row.PrintingEntity, setName, ct);

            ranked.Add(new CardCandidateResponse
            {
                Printing = printingResponse,
                CombinedScore = Math.Clamp(combined, 0.0, 1.0),
                NameScore = Math.Clamp(row.NameScore, 0.0, 1.0),
                HammingScore = Math.Clamp(row.HammingScore, 0.0, 1.0),
                HammingDistance = row.HammingDistance,
                MatchedByPHash = row.MatchedByPHash,
                MatchedByName = row.MatchedByName,
            });
        }

        ranked.Sort((a, b) => b.CombinedScore.CompareTo(a.CombinedScore));
        return ranked;
    }

    private static RecognitionConfidence ClassifyConfidence(IReadOnlyList<CardCandidateResponse> ranked)
    {
        if (ranked.Count == 0)
        {
            return RecognitionConfidence.Low;
        }

        var best = ranked[0];

        if (best.NameScore >= HighName && best.CombinedScore >= HighCombined)
        {
            return RecognitionConfidence.High;
        }

        if (best.NameScore >= MediumName && best.CombinedScore >= MediumCombined)
        {
            return RecognitionConfidence.Medium;
        }

        return RecognitionConfidence.Low;
    }

    private sealed class CombinedRow
    {
        public required string PrintingId { get; set; }

        public CardPrinting? PrintingEntity { get; set; }

        public double NameScore { get; set; }

        public double HammingScore { get; set; }

        public int? HammingDistance { get; set; }

        public bool MatchedByPHash { get; set; }

        public bool MatchedByName { get; set; }
    }
}
