using LupiraMtgApi.Data;
using LupiraMtgApi.Data.Entities;
using LupiraMtgApi.Models;
using LupiraMtgApi.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Globalization;
using LupiraMtgApi.Models.Sync;
using LupiraMtgApi.Services.Imaging;
using LupiraMtgApi.Services.Scryfall;
using LupiraMtgApi.Services.SetSymbol;
using LupiraMtgApi.Services.Storage;
namespace LupiraMtgApi.Jobs;

public sealed class ScryfallSyncRunner
{
    // OpenTelemetry plumbing for the long-running sync job. Without these the sync
    // is a black box — only `started`/`completed` log lines exist, and a 4-hour
    // sync gives no per-phase visibility. ActivitySource produces the trace tree
    // (sync.run → sync.sets, sync.set_icons, sync.printings); the meter exposes
    // throughput so dashboards can chart printings/sec or icon-fetches/sec over
    // a sync run, and counters track outcome totals across runs.
    private static readonly ActivitySource SyncActivity = new("LupiraMtgApi.Sync");
    private static readonly Meter SyncMeter = new("LupiraMtgApi.Sync");
    private static readonly Histogram<double> SyncDurationHist = SyncMeter.CreateHistogram<double>("scryfall.sync.duration_ms", unit: "ms", description: "End-to-end sync run time");
    private static readonly Histogram<double> SetsPhaseHist = SyncMeter.CreateHistogram<double>("scryfall.sync.sets.duration_ms", unit: "ms", description: "Set metadata upsert phase");
    private static readonly Histogram<double> IconsPhaseHist = SyncMeter.CreateHistogram<double>("scryfall.sync.set_icons.duration_ms", unit: "ms", description: "Per-set SVG download + rasterize phase");
    private static readonly Histogram<double> PrintingsPhaseHist = SyncMeter.CreateHistogram<double>("scryfall.sync.printings.duration_ms", unit: "ms", description: "Per-printing upsert + image + pHash phase");
    private static readonly Histogram<double> IndexRebuildHist = SyncMeter.CreateHistogram<double>("scryfall.sync.index_rebuild.duration_ms", unit: "ms", description: "Post-sync BK-tree rebuild");
    private static readonly Counter<long> PrintingsAddedCounter = SyncMeter.CreateCounter<long>("scryfall.sync.printings.added.total");
    private static readonly Counter<long> PrintingsUpdatedCounter = SyncMeter.CreateCounter<long>("scryfall.sync.printings.updated.total");
    private static readonly Counter<long> ImagesUploadedCounter = SyncMeter.CreateCounter<long>("scryfall.sync.images.uploaded.total");
    private static readonly Counter<long> IconRasterFailureCounter = SyncMeter.CreateCounter<long>("scryfall.sync.icons.failures.total");
    private static readonly Counter<long> SyncFailureCounter = SyncMeter.CreateCounter<long>("scryfall.sync.failures.total");

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ScryfallSyncOptions _options;
    private readonly ILogger<ScryfallSyncRunner> _logger;

    public ScryfallSyncRunner(
        IServiceScopeFactory scopeFactory,
        IOptions<ScryfallSyncOptions> options,
        ILogger<ScryfallSyncRunner> logger)
    {
        _scopeFactory = scopeFactory;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<SyncRunResponse> RunAsync(CancellationToken ct)
    {
        using var rootSpan = SyncActivity.StartActivity("scryfall.sync.run");
        var runStopwatch = Stopwatch.StartNew();
        var report = new SyncRunResponse
        {
            Status = "running",
            StartedAt = DateTimeOffset.UtcNow,
        };

        try
        {
            await using var scope = _scopeFactory.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<LupiraMtgDbContext>();
            var source = scope.ServiceProvider.GetRequiredService<ICardCatalogSource>();
            var images = scope.ServiceProvider.GetRequiredService<IImageStore>();
            var pHash = scope.ServiceProvider.GetRequiredService<PHashService>();
            var pHashIndex = scope.ServiceProvider.GetRequiredService<PHashIndex>();

            var symbolRasterizer = scope.ServiceProvider.GetRequiredService<SetSymbolRasterizer>();
            var symbolIndex = scope.ServiceProvider.GetRequiredService<SetSymbolIndex>();

            await images.EnsureBucketAsync(ct);

            using (var setsSpan = SyncActivity.StartActivity("scryfall.sync.sets"))
            {
                var sw = Stopwatch.StartNew();
                await SyncSetsAsync(db, source, ct);
                sw.Stop();
                SetsPhaseHist.Record(sw.Elapsed.TotalMilliseconds);
                setsSpan?.SetTag("sets.duration_ms", sw.Elapsed.TotalMilliseconds);
            }

            using (var iconsSpan = SyncActivity.StartActivity("scryfall.sync.set_icons"))
            {
                var sw = Stopwatch.StartNew();
                await this.SyncSetIconsAsync(db, source, images, symbolRasterizer, report, ct);
                sw.Stop();
                IconsPhaseHist.Record(sw.Elapsed.TotalMilliseconds);
                iconsSpan?.SetTag("icons.duration_ms", sw.Elapsed.TotalMilliseconds);
            }

            using (var printingsSpan = SyncActivity.StartActivity("scryfall.sync.printings"))
            {
                var sw = Stopwatch.StartNew();
                await this.SyncPrintingsAsync(db, source, images, pHash, report, ct);
                sw.Stop();
                PrintingsPhaseHist.Record(sw.Elapsed.TotalMilliseconds);
                printingsSpan?.SetTag("printings.total", report.PrintingsTotal);
                printingsSpan?.SetTag("printings.added", report.PrintingsAdded);
                printingsSpan?.SetTag("printings.updated", report.PrintingsUpdated);
                printingsSpan?.SetTag("printings.duration_ms", sw.Elapsed.TotalMilliseconds);
            }

            report.Status = "completed";
            report.FinishedAt = DateTimeOffset.UtcNow;
            _logger.LogInformation(
                "Scryfall sync completed: {Total} total ({Added} added, {Updated} updated), {Images} images, {PHashes} pHashes in {Duration}",
                report.PrintingsTotal,
                report.PrintingsAdded,
                report.PrintingsUpdated,
                report.ImagesUploaded,
                report.PHashesComputed,
                report.FinishedAt - report.StartedAt);

            PrintingsAddedCounter.Add(report.PrintingsAdded);
            PrintingsUpdatedCounter.Add(report.PrintingsUpdated);
            ImagesUploadedCounter.Add(report.ImagesUploaded);

            using (var rebuildSpan = SyncActivity.StartActivity("scryfall.sync.phash_index_rebuild"))
            {
                var sw = Stopwatch.StartNew();
                try
                {
                    await pHashIndex.RebuildAsync(ct);
                    sw.Stop();
                    IndexRebuildHist.Record(sw.Elapsed.TotalMilliseconds, new KeyValuePair<string, object?>("index", "phash"));
                    _logger.LogInformation("PHashIndex rebuilt in {Duration}ms", sw.Elapsed.TotalMilliseconds);
                }
                catch (Exception rebuildEx)
                {
                    _logger.LogWarning(rebuildEx, "PHashIndex rebuild after sync failed; recognition will use the previous index until next sync");
                    rebuildSpan?.SetTag("error.type", rebuildEx.GetType().Name);
                }
            }

            using (var rebuildSpan = SyncActivity.StartActivity("scryfall.sync.symbol_index_rebuild"))
            {
                var sw = Stopwatch.StartNew();
                try
                {
                    await symbolIndex.RebuildAsync(ct);
                    sw.Stop();
                    IndexRebuildHist.Record(sw.Elapsed.TotalMilliseconds, new KeyValuePair<string, object?>("index", "symbol"));
                    _logger.LogInformation("SetSymbolIndex rebuilt in {Duration}ms", sw.Elapsed.TotalMilliseconds);
                }
                catch (Exception rebuildEx)
                {
                    _logger.LogWarning(rebuildEx, "SetSymbolIndex rebuild after sync failed; set-symbol detection will use the previous index until next sync");
                    rebuildSpan?.SetTag("error.type", rebuildEx.GetType().Name);
                }
            }
        }
        catch (Exception ex)
        {
            report.Status = "failed";
            report.FinishedAt = DateTimeOffset.UtcNow;
            report.Error = ex.Message;
            _logger.LogError(ex, "Scryfall sync failed");
            rootSpan?.SetTag("error.type", ex.GetType().Name);
            SyncFailureCounter.Add(1, new KeyValuePair<string, object?>("error.type", ex.GetType().Name));
        }
        finally
        {
            runStopwatch.Stop();
            SyncDurationHist.Record(runStopwatch.Elapsed.TotalMilliseconds, new KeyValuePair<string, object?>("status", report.Status));
            rootSpan?.SetTag("sync.status", report.Status);
            rootSpan?.SetTag("sync.printings_total", report.PrintingsTotal);
            rootSpan?.SetTag("sync.printings_added", report.PrintingsAdded);
            rootSpan?.SetTag("sync.printings_updated", report.PrintingsUpdated);
            rootSpan?.SetTag("sync.images_uploaded", report.ImagesUploaded);
            rootSpan?.SetTag("sync.phashes_computed", report.PHashesComputed);
        }

        return report;
    }

    private static async Task SyncSetsAsync(LupiraMtgDbContext db, ICardCatalogSource source, CancellationToken ct)
    {
        var sets = await source.GetSetsAsync(ct);
        var existing = await db.Sets.ToDictionaryAsync(s => s.Code, ct);
        var now = DateTimeOffset.UtcNow;

        foreach (var dto in sets)
        {
            DateOnly? released = null;
            if (DateOnly.TryParseExact(dto.ReleasedAt, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d))
            {
                released = d;
            }

            if (existing.TryGetValue(dto.Code, out var entity))
            {
                entity.Name = dto.Name;
                entity.SetType = dto.SetType;
                entity.ReleasedAt = released;
                entity.CardCount = dto.CardCount;
                entity.IconSvgUri = dto.IconSvgUri;
                entity.SyncedAt = now;
            }
            else
            {
                db.Sets.Add(new ScryfallSet
                {
                    Code = dto.Code,
                    Name = dto.Name,
                    SetType = dto.SetType,
                    ReleasedAt = released,
                    CardCount = dto.CardCount,
                    IconSvgUri = dto.IconSvgUri,
                    SyncedAt = now,
                });
            }
        }

        await db.SaveChangesAsync(ct);
    }

    private async Task SyncSetIconsAsync(
        LupiraMtgDbContext db,
        ICardCatalogSource source,
        IImageStore images,
        SetSymbolRasterizer rasterizer,
        SyncRunResponse report,
        CancellationToken ct)
    {
        // Only fetch icons we don't already have. The Scryfall icon SVG URI is stable
        // for the lifetime of a set, so re-running sync after the first one is a no-op
        // for icons.
        var pending = await db.Sets
            .Where(s => s.IconObjectKey == null && s.IconSvgUri != null)
            .ToListAsync(ct);

        if (pending.Count == 0)
        {
            return;
        }

        var now = DateTimeOffset.UtcNow;
        var succeeded = 0;
        foreach (var entity in pending)
        {
            ct.ThrowIfCancellationRequested();

            try
            {
                await using var svgStream = await source.DownloadImageAsync(entity.IconSvgUri!, ct);
                using var svgBuffer = new MemoryStream();
                await svgStream.CopyToAsync(svgBuffer, ct);
                svgBuffer.Position = 0;

                var raster = await rasterizer.RasterizeAsync(svgBuffer, ct);
                var key = SetIconKey(entity.Code);
                using var pngStream = new MemoryStream(raster.PngBytes, writable: false);
                await images.PutAsync(key, pngStream, "image/png", ct);

                entity.IconObjectKey = key;
                entity.IconPHash = raster.PHash;
                entity.IconSyncedAt = now;
                succeeded++;
                report.ImagesUploaded++;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(
                    ex,
                    "Set icon rasterize/upload failed for {SetCode}; will retry on next sync",
                    entity.Code);
                IconRasterFailureCounter.Add(1, new KeyValuePair<string, object?>("error.type", ex.GetType().Name));
            }
        }

        await db.SaveChangesAsync(ct);
        _logger.LogInformation(
            "Set icons synced: {Succeeded}/{Pending}",
            succeeded,
            pending.Count);
    }

    private static string SetIconKey(string setCode) => $"sets/{setCode}/icon.png";

    private async Task SyncPrintingsAsync(
        LupiraMtgDbContext db,
        ICardCatalogSource source,
        IImageStore images,
        PHashService pHash,
        SyncRunResponse report,
        CancellationToken ct)
    {
        var entry = await source.GetDefaultCardsBulkEntryAsync(ct);
        var now = DateTimeOffset.UtcNow;
        var batch = new List<CardPrinting>(_options.BatchSize);

        await foreach (var dto in source.StreamCardsAsync(entry.DownloadUri, ct))
        {
            ct.ThrowIfCancellationRequested();

            if (dto.Digital)
            {
                continue;
            }

            report.PrintingsTotal++;

            var existing = await db.CardPrintings.FindAsync(new object?[] { dto.Id }, ct);
            var entity = existing ?? new CardPrinting
            {
                Id = dto.Id,
                OracleId = dto.OracleId ?? string.Empty,
                Name = dto.Name,
                SetCode = dto.SetCode,
                CollectorNumber = dto.CollectorNumber,
                ColorIdentity = dto.ColorIdentity,
                Rarity = dto.Rarity,
            };

            entity.Name = dto.Name;
            entity.OracleId = dto.OracleId ?? string.Empty;
            entity.SetCode = dto.SetCode;
            entity.CollectorNumber = dto.CollectorNumber;
            entity.ColorIdentity = dto.ColorIdentity;
            entity.Rarity = dto.Rarity;
            entity.Prices = MapPrices(dto.Prices);
            entity.SyncedAt = now;

            ApplyFaceFields(entity, dto);

            if (existing is null)
            {
                db.CardPrintings.Add(entity);
                report.PrintingsAdded++;
            }
            else
            {
                report.PrintingsUpdated++;
            }

            if (_options.DownloadImages && entity.ImageObjectKey is null && dto.ImageUris?.Normal is { Length: > 0 })
            {
                await UploadImageAsync(
                    images,
                    source,
                    dto.ImageUris.Normal,
                    NormalKey(dto.Id),
                    "image/jpeg",
                    ct);
                entity.ImageObjectKey = NormalKey(dto.Id);
                report.ImagesUploaded++;
            }

            if (_options.DownloadImages && entity.ImageArtCropKey is null && dto.ImageUris?.ArtCrop is { Length: > 0 })
            {
                await using var artStream = await source.DownloadImageAsync(dto.ImageUris.ArtCrop, ct);
                using var ms = new MemoryStream();
                await artStream.CopyToAsync(ms, ct);
                ms.Position = 0;
                await images.PutAsync(ArtCropKey(dto.Id), ms, "image/jpeg", ct);
                entity.ImageArtCropKey = ArtCropKey(dto.Id);
                report.ImagesUploaded++;

                if (_options.ComputePHashes && entity.ArtPHash is null)
                {
                    ms.Position = 0;
                    entity.ArtPHash = await pHash.ComputeAsync(ms, ct);
                    report.PHashesComputed++;
                }
            }

            if (report.PrintingsTotal % _options.BatchSize == 0)
            {
                await db.SaveChangesAsync(ct);
                _logger.LogInformation(
                    "Sync progress: {Total} processed ({Added} added, {Updated} updated)",
                    report.PrintingsTotal,
                    report.PrintingsAdded,
                    report.PrintingsUpdated);
            }

            if (_options.InterRequestDelayMs > 0)
            {
                await Task.Delay(_options.InterRequestDelayMs, ct);
            }
        }

        await db.SaveChangesAsync(ct);
    }

    private static async Task UploadImageAsync(
        IImageStore images,
        ICardCatalogSource source,
        string url,
        string objectKey,
        string contentType,
        CancellationToken ct)
    {
        await using var stream = await source.DownloadImageAsync(url, ct);
        using var ms = new MemoryStream();
        await stream.CopyToAsync(ms, ct);
        ms.Position = 0;
        await images.PutAsync(objectKey, ms, contentType, ct);
    }

    private static string NormalKey(string printingId) => $"printings/{printingId}/normal.jpg";

    private static string ArtCropKey(string printingId) => $"printings/{printingId}/art_crop.jpg";

    private static void ApplyFaceFields(CardPrinting entity, ScryfallCardDto dto)
    {
        var lang = string.IsNullOrWhiteSpace(dto.Lang) ? "en" : dto.Lang!;
        var layout = string.IsNullOrWhiteSpace(dto.Layout) ? "normal" : dto.Layout;

        // Multi-faced layouts (transform, modal_dfc, double_faced_token, art_series) carry
        // per-face data on card_faces[]. Use face 0 (front face) for now. The top-level
        // type_line on these is "Front // Back", which would mis-parse.
        var multiFaceLayouts = layout is "transform" or "modal_dfc" or "double_faced_token" or "art_series" or "reversible_card";
        var face = multiFaceLayouts && dto.CardFaces is { Length: > 0 } ? dto.CardFaces[0] : null;

        var typeLine = face?.TypeLine ?? dto.TypeLine;
        var printedTypeLine = face?.PrintedTypeLine ?? dto.PrintedTypeLine;
        var oracleText = face?.OracleText ?? dto.OracleText;
        var printedText = face?.PrintedText ?? dto.PrintedText;
        var power = face?.Power ?? dto.Power;
        var toughness = face?.Toughness ?? dto.Toughness;

        // Prefer the printed type line for non-English printings; fall back to canonical.
        var typeLineForParse = !string.Equals(lang, "en", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrWhiteSpace(printedTypeLine)
            ? printedTypeLine
            : typeLine;

        var (supertype, type, subtype) = TypeLineParser.Parse(typeLineForParse);
        entity.Supertype = supertype;
        entity.Type = type;
        entity.Subtype = subtype;

        // RulesText = what's printed on the card (may be localized).
        entity.RulesText = !string.IsNullOrWhiteSpace(printedText) ? printedText : oracleText;
        // OracleText = canonical English oracle, regardless of printing language.
        entity.OracleText = oracleText;

        entity.Power = power;
        entity.Toughness = toughness;
        entity.Lang = lang;
        entity.Layout = layout;
        entity.IsFoil = dto.Foil;
    }

    private static Dictionary<string, decimal>? MapPrices(ScryfallPrices? prices)
    {
        if (prices is null)
        {
            return null;
        }

        var result = new Dictionary<string, decimal>(4);
        Add(result, "usd", prices.Usd);
        Add(result, "usd_foil", prices.UsdFoil);
        Add(result, "eur", prices.Eur);
        Add(result, "eur_foil", prices.EurFoil);
        return result.Count == 0 ? null : result;
    }

    private static void Add(Dictionary<string, decimal> target, string key, string? value)
    {
        if (decimal.TryParse(value, NumberStyles.Number, CultureInfo.InvariantCulture, out var d))
        {
            target[key] = d;
        }
    }
}
