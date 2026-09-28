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

    /// <param name="seedPrintingIds">
    /// Candidates found by other means (pHash). Added to the pool after the name query so the narrowing zones
    /// (type line, rules text, P/T, collector line) can confirm or refute them.
    /// </param>
    public async Task<CardZoneScoringResult> ScoreAsync(
        CardZones zones,
        SetSymbolMatch? symbolMatch,
        IEnumerable<string> seedPrintingIds,
        CancellationToken ct)
    {
        var byPrinting = new Dictionary<string, PrintingZoneScores>(StringComparer.Ordinal);

        await ScoreNameAsync(zones.Name, byPrinting, ct);
        var nameFoundCandidates = byPrinting.Count > 0;
        foreach (var id in seedPrintingIds)
        {
            GetOrAdd(byPrinting, id);
        }

        await ScoreTypeLineAsync(zones.TypeLine, byPrinting, ct);
        await ScoreRulesTextAsync(zones.RulesText, byPrinting, fullScan: !nameFoundCandidates, ct);
        await ScorePowerToughnessAsync(zones.PowerToughness, byPrinting, ct);
        await ScoreBottomMetadataAsync(zones.BottomMetadata, symbolMatch, byPrinting, ct);

        var weights = WeightsForPresentZones(zones);
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

    private async Task ScoreNameAsync(string text, Dictionary<string, PrintingZoneScores> byPrinting, CancellationToken ct)
    {
        using var span = ZoneActivity.StartActivity("zone.score.name");
        if (string.IsNullOrWhiteSpace(text))
        {
            span?.SetTag("zone.skipped", "empty");
            return;
        }

        var trimmed = text.Trim();
        span?.SetTag("zone.input_length", trimmed.Length);
        var rows = await _db.CardPrintings
            .AsNoTracking()
            .Select(p => new { p.Id, Score = EF.Functions.TrigramsWordSimilarity(p.Name, trimmed) })
            .Where(x => x.Score > _options.NameCutoff)
            .OrderByDescending(x => x.Score)
            .Take(_options.NameTopK)
            .ToListAsync(ct);

        span?.SetTag("zone.hit_count", rows.Count);
        span?.SetTag("zone.cutoff", _options.NameCutoff);
        span?.SetTag("zone.top_k", _options.NameTopK);
        span?.SetTag("zone.best_score", rows.Count > 0 ? rows[0].Score : 0.0);

        foreach (var r in rows)
        {
            GetOrAdd(byPrinting, r.Id).NameScore = r.Score;
        }
    }

    private async Task ScoreTypeLineAsync(string text, Dictionary<string, PrintingZoneScores> byPrinting, CancellationToken ct)
    {
        using var span = ZoneActivity.StartActivity("zone.score.type_line");
        if (string.IsNullOrWhiteSpace(text))
        {
            span?.SetTag("zone.skipped", "empty");
            return;
        }

        // Narrow-only: a type line like "Instant" ties thousands of printings at 1.0, so bootstrapping from it
        // fills the pool with arbitrary cards. It only confirms candidates found by name or pHash.
        if (byPrinting.Count == 0)
        {
            span?.SetTag("zone.skipped", "no_pool");
            return;
        }

        var trimmed = text.Trim();
        var candidateIds = byPrinting.Keys.ToList();
        span?.SetTag("zone.input_length", trimmed.Length);
        span?.SetTag("zone.pool_size", candidateIds.Count);
        var rows = await _db.CardPrintings
            .AsNoTracking()
            .Where(p => p.TypeLineFull != null && candidateIds.Contains(p.Id))
            .Select(p => new { p.Id, Score = EF.Functions.TrigramsSimilarity(p.TypeLineFull!, trimmed) })
            .Where(x => x.Score > _options.TypeLineCutoff)
            .OrderByDescending(x => x.Score)
            .Take(_options.TypeLineTopK)
            .ToListAsync(ct);

        span?.SetTag("zone.hit_count", rows.Count);
        span?.SetTag("zone.cutoff", _options.TypeLineCutoff);
        span?.SetTag("zone.top_k", _options.TypeLineTopK);
        span?.SetTag("zone.best_score", rows.Count > 0 ? rows[0].Score : 0.0);

        foreach (var r in rows)
        {
            GetOrAdd(byPrinting, r.Id).TypeLineScore = r.Score;
        }
    }

    private async Task ScoreRulesTextAsync(
        string text,
        Dictionary<string, PrintingZoneScores> byPrinting,
        bool fullScan,
        CancellationToken ct)
    {
        using var span = ZoneActivity.StartActivity("zone.score.rules_text");
        if (string.IsNullOrWhiteSpace(text))
        {
            span?.SetTag("zone.skipped", "empty");
            return;
        }

        var trimmed = text.Trim();
        if (trimmed.Length < 12)
        {
            // Trigram similarity on tiny strings is unreliable; skip rather than mislead.
            span?.SetTag("zone.skipped", "too_short");
            span?.SetTag("zone.input_length", trimmed.Length);
            return;
        }

        span?.SetTag("zone.input_length", trimmed.Length);

        // word_similarity, not similarity — the OCR captures rules and flavor text together (the card prints
        // them adjacently and Florence has no concept of "rules vs. flavor"), so plain similarity() punishes the
        // length mismatch against a DB that stores only rules. word_similarity finds the best-aligning
        // subsequence and ignores the rest. Coalesce to OracleText so rows with a null RulesText (older sync, or
        // upstream missing printed_text) still score against canonical English oracle text.
        //
        // Narrow to the name + pHash pool when the name query found something: ~80K rows → a typical 25-35
        // member pool drops latency from ~900ms to <50ms. Full scan when the name found nothing, since RulesText
        // can be the only signal that finds a card with a garbled name (ink smear, foreign printing, …).
        var query = _db.CardPrintings
            .AsNoTracking()
            .Where(p => p.RulesText != null || p.OracleText != null);

        if (!fullScan && byPrinting.Count > 0)
        {
            var candidateIds = byPrinting.Keys.ToList();
            query = query.Where(p => candidateIds.Contains(p.Id));
            span?.SetTag("zone.scope", "narrowed");
            span?.SetTag("zone.pool_size", candidateIds.Count);
        }
        else
        {
            span?.SetTag("zone.scope", "full_scan");
        }

        var rows = await query
            .Select(p => new
            {
                p.Id,
                Score = EF.Functions.TrigramsWordSimilarity(p.RulesText ?? p.OracleText!, trimmed),
            })
            .Where(x => x.Score > _options.RulesTextCutoff)
            .OrderByDescending(x => x.Score)
            .Take(_options.RulesTextTopK)
            .ToListAsync(ct);

        span?.SetTag("zone.hit_count", rows.Count);
        span?.SetTag("zone.cutoff", _options.RulesTextCutoff);
        span?.SetTag("zone.top_k", _options.RulesTextTopK);
        span?.SetTag("zone.best_score", rows.Count > 0 ? rows[0].Score : 0.0);

        foreach (var r in rows)
        {
            GetOrAdd(byPrinting, r.Id).RulesTextScore = r.Score;
        }
    }

    private async Task ScorePowerToughnessAsync(string text, Dictionary<string, PrintingZoneScores> byPrinting, CancellationToken ct)
    {
        using var span = ZoneActivity.StartActivity("zone.score.power_toughness");
        if (string.IsNullOrWhiteSpace(text) || byPrinting.Count == 0)
        {
            span?.SetTag("zone.skipped", string.IsNullOrWhiteSpace(text) ? "empty" : "no_pool");
            return;
        }

        var match = PowerToughnessRegex.Match(text.Trim());
        if (!match.Success)
        {
            span?.SetTag("zone.skipped", "regex_no_match");
            return;
        }

        var power = match.Groups[1].Value;
        var toughness = match.Groups[2].Value;
        span?.SetTag("zone.parsed_power", power);
        span?.SetTag("zone.parsed_toughness", toughness);
        span?.SetTag("zone.pool_size", byPrinting.Count);

        // Only score printings already in the candidate pool — P/T alone is too weak to
        // bootstrap candidates and would balloon the union.
        var candidateIds = byPrinting.Keys.ToList();
        var rows = await _db.CardPrintings
            .AsNoTracking()
            .Where(p => candidateIds.Contains(p.Id) && (p.Power != null || p.Toughness != null))
            .Select(p => new { p.Id, p.Power, p.Toughness })
            .ToListAsync(ct);
        span?.SetTag("zone.hit_count", rows.Count);

        foreach (var r in rows)
        {
            var powerMatch = string.Equals(r.Power, power, StringComparison.OrdinalIgnoreCase);
            var toughnessMatch = string.Equals(r.Toughness, toughness, StringComparison.OrdinalIgnoreCase);
            var score = (powerMatch, toughnessMatch) switch
            {
                (true, true) => 1.0,
                (true, false) or (false, true) => 0.5,
                _ => 0.0,
            };
            if (score > 0)
            {
                GetOrAdd(byPrinting, r.Id).PowerToughnessScore = score;
            }
        }
    }

    private async Task ScoreBottomMetadataAsync(
        string text,
        SetSymbolMatch? symbolMatch,
        Dictionary<string, PrintingZoneScores> byPrinting,
        CancellationToken ct)
    {
        using var span = ZoneActivity.StartActivity("zone.score.bottom_metadata");
        span?.SetTag("zone.symbol_match", symbolMatch is not null);
        if (string.IsNullOrWhiteSpace(text))
        {
            span?.SetTag("zone.skipped", "empty");
            return;
        }

        var parsed = BottomMetadataParser.Parse(text);
        if (parsed is null)
        {
            span?.SetTag("zone.skipped", "no_collector_number");
            return;
        }

        var collectorNumber = parsed.CollectorNumber;
        var rarityName = parsed.Rarity;
        var setCode = parsed.SetCode;
        var lang = parsed.Lang;
        span?.SetTag("zone.parsed_collector", collectorNumber);
        span?.SetTag("zone.parsed_set", setCode);
        span?.SetTag("zone.parsed_lang", lang);

        // Tier 0: symbol-derived set agrees with text-derived set → metadata is authoritative.
        if (symbolMatch is not null && setCode is not null
            && string.Equals(symbolMatch.SetCode, setCode, StringComparison.OrdinalIgnoreCase))
        {
            var tier0 = await _db.CardPrintings
                .AsNoTracking()
                .Where(p => p.SetCode == symbolMatch.SetCode && p.CollectorNumber == collectorNumber)
                .Where(p => lang == null || p.Lang == lang)
                .Select(p => p.Id)
                .ToListAsync(ct);

            foreach (var id in tier0)
            {
                GetOrAdd(byPrinting, id).BottomMetadataScore = 1.0;
            }

            if (tier0.Count > 0)
            {
                return;
            }
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
        if (symbolMatch is not null && setCode is null)
        {
            var tier1Symbol = await _db.CardPrintings
                .AsNoTracking()
                .Where(p => p.SetCode == symbolMatch.SetCode && p.CollectorNumber == collectorNumber)
                .Where(p => lang == null || p.Lang == lang)
                .Select(p => p.Id)
                .ToListAsync(ct);

            foreach (var id in tier1Symbol)
            {
                GetOrAdd(byPrinting, id).BottomMetadataScore = 0.9;
            }

            if (tier1Symbol.Count > 0)
            {
                return;
            }
        }

        // Tier 1: full match on (SetCode, CollectorNumber, Lang). Authoritative.
        if (setCode is not null)
        {
            var tier1 = await _db.CardPrintings
                .AsNoTracking()
                .Where(p => p.SetCode == setCode && p.CollectorNumber == collectorNumber)
                .Where(p => lang == null || p.Lang == lang)
                .Select(p => p.Id)
                .ToListAsync(ct);

            foreach (var id in tier1)
            {
                GetOrAdd(byPrinting, id).BottomMetadataScore = 1.0;
            }

            if (tier1.Count > 0)
            {
                return;
            }
        }

        // Tier 2: collector number + rarity when set was unreadable. Pool members first — the global query is
        // capped and unordered, so it can miss the right printing entirely when the pool already holds it.
        if (rarityName is not null)
        {
            var candidateIds = byPrinting.Keys.ToList();
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

            foreach (var id in tier2)
            {
                GetOrAdd(byPrinting, id).BottomMetadataScore = 0.6;
            }

            if (tier2.Count > 0)
            {
                return;
            }
        }

        // Tier 3: collector number alone. Only score printings already in the pool — without
        // set/rarity filtering this is far too broad to bootstrap from.
        if (byPrinting.Count > 0)
        {
            var candidateIds = byPrinting.Keys.ToList();
            var tier3 = await _db.CardPrintings
                .AsNoTracking()
                .Where(p => candidateIds.Contains(p.Id) && p.CollectorNumber == collectorNumber)
                .Select(p => p.Id)
                .ToListAsync(ct);

            foreach (var id in tier3)
            {
                GetOrAdd(byPrinting, id).BottomMetadataScore = 0.3;
            }
        }
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

    private ZoneWeights WeightsForPresentZones(CardZones zones)
    {
        var floor = Math.Clamp(_options.OcrConfidenceFloor, 0.0, 1.0);

        var w = new ZoneWeights
        {
            NamePresent = !string.IsNullOrWhiteSpace(zones.Name),
            TypeLinePresent = !string.IsNullOrWhiteSpace(zones.TypeLine),
            RulesTextPresent = !string.IsNullOrWhiteSpace(zones.RulesText) && zones.RulesText.Trim().Length >= 12,
            PowerToughnessPresent = !string.IsNullOrWhiteSpace(zones.PowerToughness) && PowerToughnessRegex.IsMatch(zones.PowerToughness.Trim()),
            BottomMetadataPresent = BottomMetadataParser.Parse(zones.BottomMetadata) is not null,
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

    private static PrintingZoneScores GetOrAdd(Dictionary<string, PrintingZoneScores> byPrinting, string id)
    {
        if (!byPrinting.TryGetValue(id, out var existing))
        {
            existing = new PrintingZoneScores { PrintingId = id };
            byPrinting[id] = existing;
        }

        return existing;
    }
}
