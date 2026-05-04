using LupiraMtgApi.Services.Ocr;

namespace LupiraMtgApi.Services.Recognition;

public sealed class CardZoneClassifier
{
    // Cropped-card frame anatomy (proportional to a normalized standard MTG frame).
    // Tuned from the modern-frame layout; nudge from telemetry once we have it.
    private const double NameYLo = 0.04, NameYHi = 0.10, NameXLo = 0.05, NameXHi = 0.78;
    private const double TypeYLo = 0.555, TypeYHi = 0.605, TypeXLo = 0.05, TypeXHi = 0.78;
    private const double RulesYLo = 0.62, RulesYHi = 0.88;
    private const double PtYLo = 0.885, PtYHi = 0.925, PtXLo = 0.78, PtXHi = 0.96;
    private const double MetaYLo = 0.91, MetaYHi = 0.97, MetaXLo = 0.03, MetaXHi = 0.40;

    // Expected centroid range of OCR regions on a well-cropped card. The topmost OCR
    // region is the Name centroid (~y=0.07 of card); the bottom-most is BottomMetadata
    // (~y=0.94). Same idea on X: leftmost is the bottom-meta strip (~x=0.05), rightmost
    // is P/T or rules right edge (~x=0.95). When the cropper leaves padding around the
    // card, we rescale OCR centroids back into this space so the zone bands above
    // measure position relative to the card content, not the loose crop bbox.
    private const double CardOcrYMin = 0.07;
    private const double CardOcrYMax = 0.94;
    private const double CardOcrXMin = 0.05;
    private const double CardOcrXMax = 0.95;

    // Below this OCR-bbox extent on either axis we don't trust the bbox enough to
    // rescale — too few regions or too tight a cluster, and rescaling would amplify
    // noise. Falls back to image-relative centroids on that axis.
    private const double MinTightBboxSpan = 0.20;

    public CardZones Classify(OcrRegions regions, int imageWidth, int imageHeight, bool cropped)
    {
        if (regions.Regions.Count == 0 || imageWidth <= 0 || imageHeight <= 0)
        {
            return CardZones.Empty;
        }

        var raw = regions.Regions
            .Select(r => new
            {
                Region = r,
                RawCx = Centroid(r.QuadBox, isX: true) / imageWidth,
                RawCy = Centroid(r.QuadBox, isX: false) / imageHeight,
                AreaNormalized = QuadArea(r.QuadBox) / (imageWidth * (double) imageHeight),
            })
            .ToList();

        var (xMap, yMap) = ComputeContentMaps(raw.Select(r => (r.RawCx, r.RawCy)));

        var ranked = raw
            .Select(r => new RankedRegion(
                Region: r.Region,
                Cx: xMap(r.RawCx),
                Cy: yMap(r.RawCy),
                AreaNormalized: r.AreaNormalized))
            .ToList();

        return cropped ? ClassifyCropped(ranked) : ClassifyUncropped(ranked);
    }

    // Produces two coordinate-mapping functions that take an image-relative centroid
    // and emit a card-relative centroid. When OCR regions span enough of the image to
    // be trusted, we map [bboxMin, bboxMax] → [CardOcrMin, CardOcrMax] so the fixed
    // zone bands measure position relative to the card content. When the bbox is too
    // tight on an axis, we leave that axis as identity (image-relative).
    private static (Func<double, double> XMap, Func<double, double> YMap) ComputeContentMaps(
        IEnumerable<(double Cx, double Cy)> centroids)
    {
        double minX = double.PositiveInfinity, maxX = double.NegativeInfinity;
        double minY = double.PositiveInfinity, maxY = double.NegativeInfinity;
        foreach (var (cx, cy) in centroids)
        {
            if (cx < minX)
            {
                minX = cx;
            }

            if (cx > maxX)
            {
                maxX = cx;
            }

            if (cy < minY)
            {
                minY = cy;
            }

            if (cy > maxY)
            {
                maxY = cy;
            }
        }

        var xMap = BuildAxisMap(minX, maxX, CardOcrXMin, CardOcrXMax);
        var yMap = BuildAxisMap(minY, maxY, CardOcrYMin, CardOcrYMax);
        return (xMap, yMap);
    }

    private static Func<double, double> BuildAxisMap(double bboxMin, double bboxMax, double cardMin, double cardMax)
    {
        var span = bboxMax - bboxMin;
        if (span < MinTightBboxSpan)
        {
            return v => v;
        }

        var cardSpan = cardMax - cardMin;
        return v => cardMin + ((v - bboxMin) / span * cardSpan);
    }

    private static CardZones ClassifyCropped(List<RankedRegion> ranked)
    {
        var buckets = new Dictionary<CardZone, List<RankedRegion>>();
        foreach (var r in ranked)
        {
            var zone = ZoneForCropped(r.Cx, r.Cy);
            if (!buckets.TryGetValue(zone, out var list))
            {
                list = new List<RankedRegion>();
                buckets[zone] = list;
            }

            list.Add(r);
        }

        return BuildZones(buckets);
    }

    private static CardZones ClassifyUncropped(List<RankedRegion> ranked)
    {
        // Without a normalized frame we cannot trust fixed bands. Best effort: the largest
        // upper text block is Name; the largest lower-middle block is RulesText; the right-
        // most short token in the bottom strip is P/T; the leftmost short token in the
        // bottom strip is BottomMetadata. Type line is whatever sits between Name and Rules.
        var byArea = ranked.OrderByDescending(r => r.AreaNormalized).ToList();
        var top = byArea.Where(r => r.Cy < 0.35).Take(1).ToList();
        var bottomStrip = ranked.Where(r => r.Cy >= 0.85).ToList();
        var rules = byArea.Where(r => r.Cy is >= 0.55 and < 0.85).Take(3).ToList();
        var typeLine = byArea.Where(r => r.Cy is >= 0.45 and < 0.60 && !rules.Contains(r)).Take(1).ToList();

        var pt = bottomStrip.Where(r => r.Cx >= 0.65 && IsShortToken(r.Region.Text)).OrderByDescending(r => r.Cx).Take(1).ToList();
        var meta = bottomStrip.Where(r => r.Cx <= 0.45 && !pt.Contains(r)).OrderBy(r => r.Cx).Take(2).ToList();

        var buckets = new Dictionary<CardZone, List<RankedRegion>>
        {
            [CardZone.Name] = top,
            [CardZone.TypeLine] = typeLine,
            [CardZone.RulesText] = rules,
            [CardZone.PowerToughness] = pt,
            [CardZone.BottomMetadata] = meta,
        };

        return BuildZones(buckets);
    }

    private static CardZones BuildZones(Dictionary<CardZone, List<RankedRegion>> buckets)
    {
        return new CardZones
        {
            Name = JoinReadingOrder(buckets, CardZone.Name),
            TypeLine = JoinReadingOrder(buckets, CardZone.TypeLine),
            RulesText = JoinReadingOrder(buckets, CardZone.RulesText),
            PowerToughness = JoinReadingOrder(buckets, CardZone.PowerToughness),
            BottomMetadata = JoinReadingOrder(buckets, CardZone.BottomMetadata),
        };
    }

    private static string JoinReadingOrder(Dictionary<CardZone, List<RankedRegion>> buckets, CardZone zone)
    {
        if (!buckets.TryGetValue(zone, out var list) || list.Count == 0)
        {
            return string.Empty;
        }

        // Reading order: rows top-to-bottom, then left-to-right within each row. Two regions
        // are in the same row if their y-centroids differ by < 1.5% of image height.
        var sorted = list
            .OrderBy(r => Math.Round(r.Cy * 64))
            .ThenBy(r => r.Cx)
            .Select(r => r.Region.Text.Trim())
            .Where(t => !string.IsNullOrEmpty(t));

        return string.Join(' ', sorted);
    }

    private static CardZone ZoneForCropped(double cx, double cy)
    {
        if (Within(cx, NameXLo, NameXHi) && Within(cy, NameYLo, NameYHi))
        {
            return CardZone.Name;
        }

        if (Within(cx, TypeXLo, TypeXHi) && Within(cy, TypeYLo, TypeYHi))
        {
            return CardZone.TypeLine;
        }

        if (Within(cx, PtXLo, PtXHi) && Within(cy, PtYLo, PtYHi))
        {
            return CardZone.PowerToughness;
        }

        if (Within(cx, MetaXLo, MetaXHi) && Within(cy, MetaYLo, MetaYHi))
        {
            return CardZone.BottomMetadata;
        }

        if (Within(cy, RulesYLo, RulesYHi))
        {
            return CardZone.RulesText;
        }

        return CardZone.Unknown;
    }

    private static bool Within(double v, double lo, double hi) => v >= lo && v <= hi;

    private static bool IsShortToken(string text)
    {
        var t = text.Trim();
        return t.Length is > 0 and <= 8;
    }

    private static double Centroid(double[] quad, bool isX)
    {
        // QuadBox is x1,y1,x2,y2,x3,y3,x4,y4. Mean of the 4 vertices on the requested axis.
        var start = isX ? 0 : 1;
        return (quad[start] + quad[start + 2] + quad[start + 4] + quad[start + 6]) / 4.0;
    }

    private static double QuadArea(double[] quad)
    {
        // Shoelace formula for the polygon (x1,y1)..(x4,y4).
        var sum = 0.0;
        for (var i = 0; i < 4; i++)
        {
            var j = (i + 1) % 4;
            sum += quad[i * 2] * quad[(j * 2) + 1];
            sum -= quad[(j * 2)] * quad[(i * 2) + 1];
        }

        return Math.Abs(sum) / 2.0;
    }

    private sealed record RankedRegion(OcrRegion Region, double Cx, double Cy, double AreaNormalized);
}
