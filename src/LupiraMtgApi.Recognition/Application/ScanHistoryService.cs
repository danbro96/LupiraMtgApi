using LupiraMtgApi.Catalog.Data;
using LupiraMtgApi.Catalog.Domain;
using LupiraMtgApi.Catalog.Dtos.Cards;
using LupiraMtgApi.Catalog.Infrastructure.Storage;
using LupiraMtgApi.Catalog.Mappers;
using LupiraMtgApi.Pricing.Application;
using LupiraMtgApi.Recognition.Dtos;
using Marten;
using Microsoft.EntityFrameworkCore;

namespace LupiraMtgApi.Recognition.Application;

/// <summary>
/// Reads a user's scan history (list + detail) and hydrates the referenced printings. Owner identity
/// is resolved by the host adapter and passed in; <see cref="GetAsync"/> returns null for a missing or
/// other-owner scan so the host can map it to 404 without leaking existence.
/// </summary>
public sealed class ScanHistoryService
{
    private const int DefaultLimit = 25;
    private const int MaxLimit = 100;
    private static readonly TimeSpan ImagePresignExpiry = TimeSpan.FromMinutes(15);

    private readonly IDocumentSession _session;
    private readonly LupiraMtgDbContext _db;
    private readonly CardPrintingMapper _mapper;
    private readonly IImageStore _images;
    private readonly CardPriceLookup _prices;

    public ScanHistoryService(
        IDocumentSession session,
        LupiraMtgDbContext db,
        CardPrintingMapper mapper,
        IImageStore images,
        CardPriceLookup prices)
    {
        _session = session;
        _db = db;
        _mapper = mapper;
        _images = images;
        _prices = prices;
    }

    public async Task<ScanListResponse> ListAsync(string ownerId, int? take, int? skip, CancellationToken ct)
    {
        var clampedTake = Math.Clamp(take ?? DefaultLimit, 1, MaxLimit);
        var clampedSkip = Math.Max(skip ?? 0, 0);

        var baseQuery = _session.Query<ScanLogDocument>().Where(s => s.OwnerId == ownerId);
        var total = await Marten.QueryableExtensions.CountAsync(baseQuery, ct);

        var docs = await Marten.QueryableExtensions.ToListAsync(
            baseQuery
                .OrderByDescending(s => s.ScannedAt)
                .Skip(clampedSkip)
                .Take(clampedTake),
            ct);

        // Hydrate the top candidate's printing for each scan in one EF round-trip.
        var topPrintingIds = docs
            .Select(d => d.Candidates.FirstOrDefault()?.PrintingId)
            .OfType<string>()
            .Distinct()
            .ToList();

        var printingsById = topPrintingIds.Count == 0
            ? new Dictionary<string, CardPrinting>()
            : await _db.CardPrintings
                .AsNoTracking()
                .Where(p => topPrintingIds.Contains(p.Id))
                .ToDictionaryAsync(p => p.Id, ct);

        var setNames = printingsById.Count == 0
            ? new Dictionary<string, string>()
            : await _db.Sets
                .AsNoTracking()
                .Where(s => printingsById.Values.Select(p => p.SetCode).Contains(s.Code))
                .ToDictionaryAsync(s => s.Code, s => s.Name, ct);

        var prices = await _prices.GetAsync(printingsById.Keys, ct);

        var results = new List<ScanSummaryResponse>(docs.Count);
        foreach (var doc in docs)
        {
            var topCandidate = doc.Candidates.FirstOrDefault();
            CardPrintingResponse? topMatch = null;
            if (topCandidate is not null && printingsById.TryGetValue(topCandidate.PrintingId, out var printing))
            {
                var setName = setNames.GetValueOrDefault(printing.SetCode, printing.SetCode);
                topMatch = await _mapper.MapAsync(printing, setName, prices.GetValueOrDefault(printing.Id), ct);
            }

            results.Add(new ScanSummaryResponse
            {
                Id = doc.Id,
                ScannedAt = doc.ScannedAt,
                Confidence = doc.Confidence,
                TopMatch = topMatch,
                HasFeedback = doc.FeedbackAt.HasValue,
                FeedbackChangedTop = doc.FeedbackAt.HasValue
                    && doc.FeedbackCorrectPrintingId is { Length: > 0 }
                    && doc.FeedbackCorrectPrintingId != topCandidate?.PrintingId,
            });
        }

        return new ScanListResponse { Results = results, Total = total };
    }

    public async Task<ScanDetailResponse?> GetAsync(string ownerId, Guid scanId, CancellationToken ct)
    {
        var doc = await _session.LoadAsync<ScanLogDocument>(scanId, ct);
        if (doc is null || doc.OwnerId != ownerId)
        {
            return null;
        }

        // Hydrate every candidate's printing in one EF round-trip.
        var printingIds = doc.Candidates.Select(c => c.PrintingId).Distinct().ToList();
        var printingsById = printingIds.Count == 0
            ? new Dictionary<string, CardPrinting>()
            : await _db.CardPrintings
                .AsNoTracking()
                .Where(p => printingIds.Contains(p.Id))
                .ToDictionaryAsync(p => p.Id, ct);

        var setNames = printingsById.Count == 0
            ? new Dictionary<string, string>()
            : await _db.Sets
                .AsNoTracking()
                .Where(s => printingsById.Values.Select(p => p.SetCode).Contains(s.Code))
                .ToDictionaryAsync(s => s.Code, s => s.Name, ct);

        var prices = await _prices.GetAsync(printingsById.Keys, ct);

        var candidates = new List<CardCandidateResponse>(doc.Candidates.Count);
        foreach (var c in doc.Candidates)
        {
            if (!printingsById.TryGetValue(c.PrintingId, out var printing))
            {
                // Printing was synced out of existence — skip rather than 500.
                continue;
            }

            var setName = setNames.GetValueOrDefault(printing.SetCode, printing.SetCode);
            candidates.Add(new CardCandidateResponse
            {
                Printing = await _mapper.MapAsync(printing, setName, prices.GetValueOrDefault(printing.Id), ct),
                CombinedScore = c.CombinedScore,
                OcrAggregateScore = c.OcrAggregateScore,
                NameScore = c.NameScore,
                TypeLineScore = c.TypeLineScore,
                RulesTextScore = c.RulesTextScore,
                PowerToughnessScore = c.PowerToughnessScore,
                BottomMetadataScore = c.BottomMetadataScore,
                HammingScore = c.HammingScore,
                SetTypeWeight = c.SetTypeWeight,
                HammingDistance = c.HammingDistance,
                MatchedByPHash = c.MatchedByPHash,
                MatchedByName = c.MatchedByName,
            });
        }

        string? imageUrl = null;
        if (doc.ImageObjectKey is { Length: > 0 })
        {
            imageUrl = await _images.CreatePresignedGetUrlAsync(doc.ImageObjectKey, ImagePresignExpiry, ct);
        }

        ScanSetSymbol? setSymbol = null;
        if (doc.DetectedSetCode is { Length: > 0 } && doc.DetectedSetSymbolHamming is int hamming)
        {
            setSymbol = new ScanSetSymbol
            {
                SetCode = doc.DetectedSetCode,
                HammingDistance = hamming,
                // ScanLogDocument doesn't persist the per-symbol score; report 0 — the hamming
                // distance is the load-bearing signal anyway.
                Score = 0,
            };
        }

        ScanFeedbackInfo? feedback = null;
        if (doc.FeedbackAt is DateTimeOffset feedbackAt && doc.FeedbackCorrectPrintingId is { Length: > 0 })
        {
            feedback = new ScanFeedbackInfo
            {
                CorrectPrintingId = doc.FeedbackCorrectPrintingId,
                CorrectPrintingRank = doc.FeedbackCorrectPrintingRank,
                At = feedbackAt,
            };
        }

        var ocrZones = new ScanZoneTexts
        {
            // Per-zone OCR confidences aren't persisted — zero them. The texts are the
            // load-bearing data here.
            Name = doc.OcrName ?? string.Empty,
            TypeLine = doc.OcrTypeLine ?? string.Empty,
            RulesText = doc.OcrRulesText ?? string.Empty,
            PowerToughness = doc.OcrPowerToughness ?? string.Empty,
            BottomMetadata = doc.OcrBottomMetadata ?? string.Empty,
            NameConfidence = 0,
            TypeLineConfidence = 0,
            RulesTextConfidence = 0,
            PowerToughnessConfidence = 0,
            BottomMetadataConfidence = 0,
        };

        return new ScanDetailResponse
        {
            Id = doc.Id,
            ScannedAt = doc.ScannedAt,
            Confidence = doc.Confidence,
            ImageUrl = imageUrl,
            ImageMediaType = doc.ImageMediaType,
            OcrZones = ocrZones,
            SetSymbol = setSymbol,
            Candidates = candidates,
            Feedback = feedback,
        };
    }
}
