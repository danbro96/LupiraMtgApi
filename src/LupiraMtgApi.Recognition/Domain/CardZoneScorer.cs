using System.Diagnostics;
using System.Text.RegularExpressions;
using LupiraMtgApi.Catalog.Data;
using LupiraMtgApi.Recognition.Infrastructure.SetSymbol;
using Microsoft.EntityFrameworkCore;

namespace LupiraMtgApi.Recognition.Domain;

public sealed class CardZoneScorer
{
    // Reuses the LupiraMtgApi.Scans source so per-zone spans nest under the parent
    // `zone.score` span set by ScanHandler. Tagging each query with its hit count and
    // the input text length lets us see at-a-glance which zones are the bottleneck on
    // a slow scan and which cutoffs are too tight (zero hits = candidate filter
    // possibly too aggressive, hits == TopK = scan got truncated).
    private static readonly ActivitySource ZoneActivity = new("LupiraMtgApi.Scans");

    // Tolerates inline whitespace and U+2044 fraction slash; not anchored so OCR noise
    // around the P/T cluster (e.g. mana-cost glyphs misread as digits) doesn't kill the
    // match. The match itself locks onto a `<P>/<T>` pair, which is enough.
    private static readonly Regex PowerToughnessRegex = new(
        @"(\*|\d+(?:\+\*)?)\s*[\/⁄]\s*(\*|\d+(?:\+\*)?)",
        RegexOptions.Compiled);

    private readonly LupiraMtgDbContext _db;
    private readonly ScanScoringOptions _options;

    public CardZoneScorer(LupiraMtgDbContext db, IOptions<ScanScoringOptions> options)
    {
        _db = db;
        _options = options.Value;
    }

    /// <summary>
    /// Two phases: <see cref="BuildPoolAsync"/> collects candidates from the zones that can search the whole catalog
    /// (name, rules text, collector line) plus the pHash seeds; <see cref="ScorePoolAsync"/> then scores every zone
    /// over that fixed pool, so a candidate is scored the same whichever zone found it.
    /// </summary>
    public async Task<CardZoneScoringResult> ScoreAsync(
        CardZones zones,
        SetSymbolMatch? symbolMatch,
        IEnumerable<string> seedPrintingIds,
        CancellationToken ct)
    {
        var bottomMetadata = BottomMetadataParser.Parse(zones.BottomMetadata);
        var pool = await BuildPoolAsync(zones, bottomMetadata, symbolMatch, seedPrintingIds, ct);
        var byPrinting = await ScorePoolAsync(pool, zones, bottomMetadata, ct);

        var weights = WeightsForPresentZones(zones, bottomMetadata);
        foreach (var scores in byPrinting.Values)
        {
            scores.AggregateScore = ComputeAggregate(scores, weights);
        }

        return new CardZoneScoringResult
        {
            ByPrinting = byPrinting,
            Weights = weights,
        };
    }

    private async Task<CandidatePool> BuildPoolAsync(
        CardZones zones,
        BottomMetadata? bottomMetadata,
        SetSymbolMatch? symbolMatch,
        IEnumerable<string> seedPrintingIds,
        CancellationToken ct)
    {
        var pool = new CandidatePool();
        await FindByNameAsync(zones.Name, pool, ct);
        var nameFoundCandidates = pool.Ids.Count > 0;
        pool.Ids.UnionWith(seedPrintingIds);

        // RulesText can be the only signal that finds a card with a garbled name (ink smear, foreign printing, …),
        // but a full scan costs ~900ms, so only when the name found nothing.
        if (!nameFoundCandidates)
        {
            await FindByRulesTextAsync(zones.RulesText, pool, ct);
        }

        await FindByBottomMetadataAsync(bottomMetadata, symbolMatch, pool, ct);
        return pool;
    }

    private async Task FindByNameAsync(string text, CandidatePool pool, CancellationToken ct)
    {
        using var span = ZoneActivity.StartActivity("zone.score.name");
        if (string.IsNullOrWhiteSpace(text))
        {
            span?.SetTag("zone.skipped", "empty");
            return;
        }

        var trimmed = text.Trim();
        span?.SetTag("zone.input_length", trimmed.Length);
        var ids = await _db.CardPrintings
            .AsNoTracking()
            .Select(p => new { p.Id, Score = EF.Functions.TrigramsWordSimilarity(p.Name, trimmed) })
            .Where(x => x.Score > _options.NameCutoff)
            .OrderByDescending(x => x.Score)
            .Take(_options.NameTopK)
            .Select(x => x.Id)
            .ToListAsync(ct);

        span?.SetTag("zone.hit_count", ids.Count);
        span?.SetTag("zone.cutoff", _options.NameCutoff);
        span?.SetTag("zone.top_k", _options.NameTopK);
        pool.Ids.UnionWith(ids);
    }

    private async Task FindByRulesTextAsync(string text, CandidatePool pool, CancellationToken ct)
    {
        using var span = ZoneActivity.StartActivity("zone.score.rules_text");
        var trimmed = RulesTextOrNull(text);
        if (trimmed is null)
        {
            span?.SetTag("zone.skipped", "too_short");
            return;
        }

        span?.SetTag("zone.input_length", trimmed.Length);
        span?.SetTag("zone.scope", "full_scan");

        // word_similarity, not similarity: OCR captures rules and flavor text together, and word_similarity scores
        // the best-aligning subsequence instead of punishing the length mismatch. OracleText covers rows without
        // printed rules text (older sync, upstream missing printed_text).
        var ids = await _db.CardPrintings
            .AsNoTracking()
            .Where(p => p.RulesText != null || p.OracleText != null)
            .Select(p => new { p.Id, Score = EF.Functions.TrigramsWordSimilarity(p.RulesText ?? p.OracleText!, trimmed) })
            .Where(x => x.Score > _options.RulesTextCutoff)
            .OrderByDescending(x => x.Score)
            .Take(_options.RulesTextTopK)
            .Select(x => x.Id)
            .ToListAsync(ct);

        span?.SetTag("zone.hit_count", ids.Count);
        span?.SetTag("zone.cutoff", _options.RulesTextCutoff);
        span?.SetTag("zone.top_k", _options.RulesTextTopK);
        pool.Ids.UnionWith(ids);
    }

    /// <summary>
    /// Collector tiers 0–2, strongest first; the first tier with a hit wins. Tier 3 (collector number alone) is
    /// too broad to search the catalog with, so <see cref="ScorePoolAsync"/> applies it to the pool when no tier hit.
    /// </summary>
    private async Task FindByBottomMetadataAsync(
        BottomMetadata? parsed,
        SetSymbolMatch? symbolMatch,
        CandidatePool pool,
        CancellationToken ct)
    {
        using var span = ZoneActivity.StartActivity("zone.score.bottom_metadata");
        span?.SetTag("zone.symbol_match", symbolMatch is not null);
        if (parsed is null)
        {
            span?.SetTag("zone.skipped", "no_collector_number");
            return;
        }

        var setCode = parsed.SetCode;
        span?.SetTag("zone.parsed_collector", parsed.CollectorNumber);
        span?.SetTag("zone.parsed_set", setCode);
        span?.SetTag("zone.parsed_lang", parsed.Lang);

        // Tier 0: symbol-derived set agrees with text-derived set → metadata is authoritative.
        if (symbolMatch is not null && setCode is not null
            && string.Equals(symbolMatch.SetCode, setCode, StringComparison.OrdinalIgnoreCase)
            && await FindBySetAndCollectorAsync(symbolMatch.SetCode, parsed, 1.0, pool, ct))
        {
            return;
        }

        // Symbol disagrees with OCR set → drop the OCR set so Tier 1 doesn't lock to a
        // mis-OCR'd 3-letter blob. Falls through to Tier 2 (collector + rarity).
        if (symbolMatch is not null && setCode is not null
            && !string.Equals(symbolMatch.SetCode, setCode, StringComparison.OrdinalIgnoreCase))
        {
            setCode = null;
        }

        // Symbol matched but OCR didn't read a set code → use the symbol's set as a Tier-1
        // driver. One signal missing vs. Tier 0 → slight discount.
        if (symbolMatch is not null && setCode is null
            && await FindBySetAndCollectorAsync(symbolMatch.SetCode, parsed, 0.9, pool, ct))
        {
            return;
        }

        // Tier 1: full match on (SetCode, CollectorNumber, Lang). Authoritative.
        if (setCode is not null
            && await FindBySetAndCollectorAsync(setCode, parsed, 1.0, pool, ct))
        {
            return;
        }

        // Tier 2: collector number + rarity when set was unreadable. Pool members first — the global query is
        // capped and unordered, so it can miss the right printing entirely when the pool already holds it.
        var collectorNumber = parsed.CollectorNumber;
        var rarityName = parsed.Rarity;
        if (rarityName is not null)
        {
            var candidateIds = pool.Ids.ToList();
            var tier2 = candidateIds.Count > 0
                ? await _db.CardPrintings
                    .AsNoTracking()
                    .Where(p => candidateIds.Contains(p.Id) && p.CollectorNumber == collectorNumber && p.Rarity == rarityName)
                    .Select(p => p.Id)
                    .ToListAsync(ct)
                : [];

            if (tier2.Count == 0)
            {
                tier2 = await _db.CardPrintings
                    .AsNoTracking()
                    .Where(p => p.CollectorNumber == collectorNumber && p.Rarity == rarityName)
                    .Take(50)
                    .Select(p => p.Id)
                    .ToListAsync(ct);
            }

            pool.AddBottomMetadataMatches(tier2, 0.6);
        }
    }

    private async Task<bool> FindBySetAndCollectorAsync(
        string setCode,
        BottomMetadata parsed,
        double score,
        CandidatePool pool,
        CancellationToken ct)
    {
        var collectorNumber = parsed.CollectorNumber;
        var lang = parsed.Lang;
        var ids = await _db.CardPrintings
            .AsNoTracking()
            .Where(p => p.SetCode == setCode && p.CollectorNumber == collectorNumber)
            .Where(p => lang == null || p.Lang == lang)
            .Select(p => p.Id)
            .ToListAsync(ct);

        return pool.AddBottomMetadataMatches(ids, score);
    }

    private async Task<Dictionary<string, PrintingZoneScores>> ScorePoolAsync(
        CandidatePool pool,
        CardZones zones,
        BottomMetadata? bottomMetadata,
        CancellationToken ct)
    {
        var byPrinting = new Dictionary<string, PrintingZoneScores>(StringComparer.Ordinal);
        using var span = ZoneActivity.StartActivity("zone.score.pool");
        span?.SetTag("zone.pool_size", pool.Ids.Count);
        if (pool.Ids.Count == 0)
        {
            return byPrinting;
        }

        var name = zones.Name?.Trim() ?? string.Empty;
        var typeLine = zones.TypeLine?.Trim() ?? string.Empty;
        var rulesText = RulesTextOrNull(zones.RulesText) ?? string.Empty;
        var powerToughness = string.IsNullOrWhiteSpace(zones.PowerToughness)
            ? null
            : PowerToughnessRegex.Match(zones.PowerToughness.Trim());

        // Pool-sized (≲135 ids), so every similarity is computed in one round trip and cut off in memory.
        var candidateIds = pool.Ids.ToList();
        var rows = await _db.CardPrintings
            .AsNoTracking()
            .Where(p => candidateIds.Contains(p.Id))
            .Select(p => new
            {
                p.Id,
                Name = EF.Functions.TrigramsWordSimilarity(p.Name, name),
                TypeLine = EF.Functions.TrigramsSimilarity(p.TypeLineFull ?? string.Empty, typeLine),
                RulesText = EF.Functions.TrigramsWordSimilarity(p.RulesText ?? p.OracleText ?? string.Empty, rulesText),
                p.Power,
                p.Toughness,
                p.CollectorNumber,
            })
            .ToListAsync(ct);

        var tier3 = bottomMetadata is not null && pool.BottomMetadataScores.Count == 0;
        foreach (var r in rows)
        {
            var bottomScore = pool.BottomMetadataScores.TryGetValue(r.Id, out var tierScore) ? tierScore
                : tier3 && r.CollectorNumber == bottomMetadata!.CollectorNumber ? 0.3
                : 0.0;
            byPrinting[r.Id] = new PrintingZoneScores
            {
                PrintingId = r.Id,
                NameScore = name.Length > 0 && r.Name > _options.NameCutoff ? r.Name : 0.0,
                TypeLineScore = typeLine.Length > 0 && r.TypeLine > _options.TypeLineCutoff ? r.TypeLine : 0.0,
                RulesTextScore = rulesText.Length > 0 && r.RulesText > _options.RulesTextCutoff ? r.RulesText : 0.0,
                PowerToughnessScore = powerToughness is { Success: true } ? ScorePowerToughness(powerToughness, r.Power, r.Toughness) : 0.0,
                BottomMetadataScore = bottomScore,
            };
        }

        span?.SetTag("zone.hit_count", rows.Count);
        TagZone(span, "name", byPrinting.Values.Select(s => s.NameScore));
        TagZone(span, "type_line", byPrinting.Values.Select(s => s.TypeLineScore));
        TagZone(span, "rules_text", byPrinting.Values.Select(s => s.RulesTextScore));
        TagZone(span, "power_toughness", byPrinting.Values.Select(s => s.PowerToughnessScore));
        TagZone(span, "bottom_metadata", byPrinting.Values.Select(s => s.BottomMetadataScore));
        return byPrinting;
    }

    private static double ScorePowerToughness(Match parsed, string? power, string? toughness)
    {
        var powerMatch = string.Equals(power, parsed.Groups[1].Value, StringComparison.OrdinalIgnoreCase);
        var toughnessMatch = string.Equals(toughness, parsed.Groups[2].Value, StringComparison.OrdinalIgnoreCase);
        return (powerMatch, toughnessMatch) switch
        {
            (true, true) => 1.0,
            (true, false) or (false, true) => 0.5,
            _ => 0.0,
        };
    }

    private static void TagZone(Activity? span, string zone, IEnumerable<double> scores)
    {
        if (span is null)
        {
            return;
        }

        var matched = scores.Where(s => s > 0).ToList();
        span.SetTag($"zone.{zone}.hit_count", matched.Count);
        span.SetTag($"zone.{zone}.best_score", matched.Count > 0 ? matched.Max() : 0.0);
    }

    // Trigram similarity on tiny strings is unreliable; skip rather than mislead.
    private static string? RulesTextOrNull(string? text)
    {
        var trimmed = text?.Trim();
        return trimmed is { Length: >= 12 } ? trimmed : null;
    }

    private double ComputeAggregate(PrintingZoneScores scores, ZoneWeights weights)
    {
        if (weights.TotalPresent <= 0)
        {
            return 0.0;
        }

        // Weights are already confidence-smoothed in WeightsForPresentZones; here we
        // just normalize across the present zones so the aggregate stays in [0,1].
        var sum = 0.0;
        if (weights.NamePresent)
        {
            sum += (weights.NameWeight / weights.TotalPresent) * scores.NameScore;
        }

        if (weights.TypeLinePresent)
        {
            sum += (weights.TypeLineWeight / weights.TotalPresent) * scores.TypeLineScore;
        }

        if (weights.RulesTextPresent)
        {
            sum += (weights.RulesTextWeight / weights.TotalPresent) * scores.RulesTextScore;
        }

        if (weights.PowerToughnessPresent)
        {
            sum += (weights.PowerToughnessWeight / weights.TotalPresent) * scores.PowerToughnessScore;
        }

        if (weights.BottomMetadataPresent)
        {
            sum += (weights.BottomMetadataWeight / weights.TotalPresent) * scores.BottomMetadataScore;
        }

        return Math.Clamp(sum, 0.0, 1.0);
    }

    private ZoneWeights WeightsForPresentZones(CardZones zones, BottomMetadata? bottomMetadata)
    {
        var floor = Math.Clamp(_options.OcrConfidenceFloor, 0.0, 1.0);

        var w = new ZoneWeights
        {
            NamePresent = !string.IsNullOrWhiteSpace(zones.Name),
            TypeLinePresent = !string.IsNullOrWhiteSpace(zones.TypeLine),
            RulesTextPresent = !string.IsNullOrWhiteSpace(zones.RulesText) && zones.RulesText.Trim().Length >= 12,
            PowerToughnessPresent = !string.IsNullOrWhiteSpace(zones.PowerToughness) && PowerToughnessRegex.IsMatch(zones.PowerToughness.Trim()),
            BottomMetadataPresent = bottomMetadata is not null,
        };

        // Smooth zone weights by per-zone OCR confidence: effective = base * (floor + (1-floor)*confidence).
        // Confident reads keep their full base weight; noisy reads still contribute (down to `floor`)
        // because trigram match can recover the right card even from garbled text.
        w.NameWeight = w.NamePresent ? _options.NameWeight * Smooth(zones.NameConfidence, floor) : 0.0;
        w.TypeLineWeight = w.TypeLinePresent ? _options.TypeLineWeight * Smooth(zones.TypeLineConfidence, floor) : 0.0;
        w.RulesTextWeight = w.RulesTextPresent ? _options.RulesTextWeight * Smooth(zones.RulesTextConfidence, floor) : 0.0;
        w.PowerToughnessWeight = w.PowerToughnessPresent ? _options.PowerToughnessWeight * Smooth(zones.PowerToughnessConfidence, floor) : 0.0;
        w.BottomMetadataWeight = w.BottomMetadataPresent ? _options.BottomMetadataWeight * Smooth(zones.BottomMetadataConfidence, floor) : 0.0;

        w.TotalPresent = w.NameWeight + w.TypeLineWeight + w.RulesTextWeight + w.PowerToughnessWeight + w.BottomMetadataWeight;
        return w;
    }

    private static double Smooth(double confidence, double floor)
    {
        var c = Math.Clamp(confidence, 0.0, 1.0);
        return floor + ((1.0 - floor) * c);
    }

    private sealed class CandidatePool
    {
        public HashSet<string> Ids { get; } = new(StringComparer.Ordinal);

        public Dictionary<string, double> BottomMetadataScores { get; } = new(StringComparer.Ordinal);

        public bool AddBottomMetadataMatches(List<string> ids, double score)
        {
            foreach (var id in ids)
            {
                Ids.Add(id);
                BottomMetadataScores[id] = score;
            }

            return ids.Count > 0;
        }
    }
}
