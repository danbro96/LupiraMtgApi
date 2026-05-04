using LupiraMtgApi.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using System.Text.RegularExpressions;
using LupiraMtgApi.Services.SetSymbol;
namespace LupiraMtgApi.Services.Recognition;

public sealed class CardZoneScorer
{
    // Tolerates inline whitespace and U+2044 fraction slash; not anchored so OCR noise
    // around the P/T cluster (e.g. mana-cost glyphs misread as digits) doesn't kill the
    // match. The match itself locks onto a `<P>/<T>` pair, which is enough.
    private static readonly Regex PowerToughnessRegex = new(
        @"(\*|\d+(?:\+\*)?)\s*[\/⁄]\s*(\*|\d+(?:\+\*)?)",
        RegexOptions.Compiled);

    // Collector "229/254" plus optional rarity letter (only C/U/R/M/S are stamped on cards).
    private static readonly Regex CollectorRegex = new(
        @"(?<num>\d{1,4})\s*[\/]\s*(?<total>\d{1,4})\s*(?<rarity>[CURMS])?",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    // Set + lang line, e.g. "THB • EN" or "STD-EN". Accepts U+2022 bullet, U+00B7 middle
    // dot, ASCII hyphen, U+2010, and U+2012.
    private static readonly Regex SetLangRegex = new(
        @"(?<set>[A-Z0-9]{2,5})\s*[•·\-‐‒]\s*(?<lang>[A-Z]{2,3})",
        RegexOptions.Compiled);

    private readonly LupiraMtgDbContext _db;
    private readonly ScanScoringOptions _options;

    public CardZoneScorer(LupiraMtgDbContext db, IOptions<ScanScoringOptions> options)
    {
        _db = db;
        _options = options.Value;
    }

    public async Task<CardZoneScoringResult> ScoreAsync(
        CardZones zones,
        SetSymbolMatch? symbolMatch,
        CancellationToken ct)
    {
        var byPrinting = new Dictionary<string, PrintingZoneScores>(StringComparer.Ordinal);

        await ScoreNameAsync(zones.Name, byPrinting, ct);
        await ScoreTypeLineAsync(zones.TypeLine, byPrinting, ct);
        await ScoreRulesTextAsync(zones.RulesText, byPrinting, ct);
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
        if (string.IsNullOrWhiteSpace(text))
        {
            return;
        }

        var trimmed = text.Trim();
        var rows = await _db.CardPrintings
            .AsNoTracking()
            .Select(p => new { p.Id, Score = EF.Functions.TrigramsWordSimilarity(p.Name, trimmed) })
            .Where(x => x.Score > _options.NameCutoff)
            .OrderByDescending(x => x.Score)
            .Take(_options.NameTopK)
            .ToListAsync(ct);

        foreach (var r in rows)
        {
            GetOrAdd(byPrinting, r.Id).NameScore = r.Score;
        }
    }

    private async Task ScoreTypeLineAsync(string text, Dictionary<string, PrintingZoneScores> byPrinting, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return;
        }

        var trimmed = text.Trim();
        var rows = await _db.CardPrintings
            .AsNoTracking()
            .Where(p => p.TypeLineFull != null)
            .Select(p => new { p.Id, Score = EF.Functions.TrigramsSimilarity(p.TypeLineFull!, trimmed) })
            .Where(x => x.Score > _options.TypeLineCutoff)
            .OrderByDescending(x => x.Score)
            .Take(_options.TypeLineTopK)
            .ToListAsync(ct);

        foreach (var r in rows)
        {
            GetOrAdd(byPrinting, r.Id).TypeLineScore = r.Score;
        }
    }

    private async Task ScoreRulesTextAsync(string text, Dictionary<string, PrintingZoneScores> byPrinting, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return;
        }

        var trimmed = text.Trim();
        if (trimmed.Length < 12)
        {
            // Trigram similarity on tiny strings is unreliable; skip rather than mislead.
            return;
        }

        var rows = await _db.CardPrintings
            .AsNoTracking()
            .Where(p => p.RulesText != null)
            .Select(p => new { p.Id, Score = EF.Functions.TrigramsSimilarity(p.RulesText!, trimmed) })
            .Where(x => x.Score > _options.RulesTextCutoff)
            .OrderByDescending(x => x.Score)
            .Take(_options.RulesTextTopK)
            .ToListAsync(ct);

        foreach (var r in rows)
        {
            GetOrAdd(byPrinting, r.Id).RulesTextScore = r.Score;
        }
    }

    private async Task ScorePowerToughnessAsync(string text, Dictionary<string, PrintingZoneScores> byPrinting, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(text) || byPrinting.Count == 0)
        {
            return;
        }

        var match = PowerToughnessRegex.Match(text.Trim());
        if (!match.Success)
        {
            return;
        }

        var power = match.Groups[1].Value;
        var toughness = match.Groups[2].Value;

        // Only score printings already in the candidate pool — P/T alone is too weak to
        // bootstrap candidates and would balloon the union.
        var candidateIds = byPrinting.Keys.ToList();
        var rows = await _db.CardPrintings
            .AsNoTracking()
            .Where(p => candidateIds.Contains(p.Id) && (p.Power != null || p.Toughness != null))
            .Select(p => new { p.Id, p.Power, p.Toughness })
            .ToListAsync(ct);

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
        if (string.IsNullOrWhiteSpace(text))
        {
            return;
        }

        var collectorMatch = CollectorRegex.Match(text);
        if (!collectorMatch.Success)
        {
            return;
        }

        var collectorNumber = collectorMatch.Groups["num"].Value.TrimStart('0');
        if (string.IsNullOrEmpty(collectorNumber))
        {
            collectorNumber = collectorMatch.Groups["num"].Value;
        }

        var rarityLetter = collectorMatch.Groups["rarity"].Success ? collectorMatch.Groups["rarity"].Value.ToUpperInvariant() : null;

        var setLangMatch = SetLangRegex.Match(text);
        var setCode = setLangMatch.Success ? setLangMatch.Groups["set"].Value.ToLowerInvariant() : null;
        var lang = setLangMatch.Success ? setLangMatch.Groups["lang"].Value.ToLowerInvariant() : null;

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

        // Tier 2: collector number + rarity letter when set was unreadable. Skip when the
        // OCR'd letter doesn't map to a known Scryfall rarity — guessing produces 0.6
        // false-positives that poison the ranking.
        if (rarityLetter is not null)
        {
            var rarityName = MapRarityLetter(rarityLetter);
            if (rarityName is not null)
            {
                var tier2 = await _db.CardPrintings
                    .AsNoTracking()
                    .Where(p => p.CollectorNumber == collectorNumber && p.Rarity == rarityName)
                    .Take(50)
                    .Select(p => p.Id)
                    .ToListAsync(ct);

                foreach (var id in tier2)
                {
                    GetOrAdd(byPrinting, id).BottomMetadataScore = 0.6;
                }

                if (tier2.Count > 0)
                {
                    return;
                }
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

        var sum = 0.0;
        if (weights.NamePresent)
        {
            sum += (_options.NameWeight / weights.TotalPresent) * scores.NameScore;
        }

        if (weights.TypeLinePresent)
        {
            sum += (_options.TypeLineWeight / weights.TotalPresent) * scores.TypeLineScore;
        }

        if (weights.RulesTextPresent)
        {
            sum += (_options.RulesTextWeight / weights.TotalPresent) * scores.RulesTextScore;
        }

        if (weights.PowerToughnessPresent)
        {
            sum += (_options.PowerToughnessWeight / weights.TotalPresent) * scores.PowerToughnessScore;
        }

        if (weights.BottomMetadataPresent)
        {
            sum += (_options.BottomMetadataWeight / weights.TotalPresent) * scores.BottomMetadataScore;
        }

        return Math.Clamp(sum, 0.0, 1.0);
    }

    private ZoneWeights WeightsForPresentZones(CardZones zones)
    {
        var w = new ZoneWeights
        {
            NamePresent = !string.IsNullOrWhiteSpace(zones.Name),
            TypeLinePresent = !string.IsNullOrWhiteSpace(zones.TypeLine),
            RulesTextPresent = !string.IsNullOrWhiteSpace(zones.RulesText) && zones.RulesText.Trim().Length >= 12,
            PowerToughnessPresent = !string.IsNullOrWhiteSpace(zones.PowerToughness) && PowerToughnessRegex.IsMatch(zones.PowerToughness.Trim()),
            BottomMetadataPresent = !string.IsNullOrWhiteSpace(zones.BottomMetadata) && CollectorRegex.IsMatch(zones.BottomMetadata),
        };

        var total = 0.0;
        if (w.NamePresent)
        {
            total += _options.NameWeight;
        }

        if (w.TypeLinePresent)
        {
            total += _options.TypeLineWeight;
        }

        if (w.RulesTextPresent)
        {
            total += _options.RulesTextWeight;
        }

        if (w.PowerToughnessPresent)
        {
            total += _options.PowerToughnessWeight;
        }

        if (w.BottomMetadataPresent)
        {
            total += _options.BottomMetadataWeight;
        }

        w.TotalPresent = total;
        return w;
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

    private static string? MapRarityLetter(string letter)
    {
        return letter switch
        {
            "C" => "common",
            "U" => "uncommon",
            "R" => "rare",
            "M" => "mythic",
            "S" => "special",
            _ => null,
        };
    }
}
