using LupiraMtgApi.Catalog.Data;
using LupiraMtgApi.Catalog.Domain;
using LupiraMtgApi.Catalog.Dtos.Cards;
using LupiraMtgApi.Catalog.Mappers;
using LupiraMtgApi.Pricing.Application;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace LupiraMtgApi.Catalog.Application;

public sealed class CardCatalogService
{
    private const int DefaultLimit = 25;
    private const int MaxLimit = 100;

    private readonly LupiraMtgDbContext _db;
    private readonly CardPrintingMapper _mapper;
    private readonly CardPriceLookup _prices;

    public CardCatalogService(LupiraMtgDbContext db, CardPrintingMapper mapper, CardPriceLookup prices)
    {
        _db = db;
        _mapper = mapper;
        _prices = prices;
    }

    public async Task<CardListResponse> ListCardsAsync(CardListRequest query, CancellationToken ct)
    {
        var filters = CardListFilters.From(query);

        var rows = await QueryRepresentativesAsync(filters, ct);
        var total = await CountOraclesAsync(filters, ct);

        var results = new List<CardResponse>(rows.Count);
        foreach (var row in rows)
        {
            results.Add(await BuildCardResponseAsync(row, ct));
        }

        return new CardListResponse { Results = results, Total = total };
    }

    private sealed record CardListFilters(
        string? Q,
        string? Set,
        string? Color,
        string[]? Colors,
        string? Rarity,
        string? Type,
        float? Cmc,
        float? CmcMin,
        float? CmcMax,
        string? Power,
        string? Toughness,
        SortKey Sort,
        bool Ascending,
        int Take,
        int Skip)
    {
        public static CardListFilters From(CardListRequest q)
        {
            var qParam = string.IsNullOrWhiteSpace(q.Q) ? null : q.Q.Trim();
            var sortKey = ParseSort(q.Sort, hasQuery: qParam is not null);
            // For relevance sort, "asc" doesn't make sense (best match first is descending similarity).
            // For everything else, default to ascending unless `order=desc`.
            var asc = sortKey switch
            {
                SortKey.Relevance => false,
                SortKey.ReleasedAt => !string.Equals(q.Order, "asc", StringComparison.OrdinalIgnoreCase),
                _ => !string.Equals(q.Order, "desc", StringComparison.OrdinalIgnoreCase),
            };

            var colorsArr = string.IsNullOrWhiteSpace(q.Colors)
                ? null
                : q.Colors.Split(new[] { ',', ' ' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                    .Select(c => c.ToUpperInvariant())
                    .Distinct()
                    .ToArray();

            return new CardListFilters(
                Q: qParam,
                Set: string.IsNullOrWhiteSpace(q.Set) ? null : q.Set.ToLowerInvariant(),
                Color: string.IsNullOrWhiteSpace(q.Color) ? null : q.Color.ToUpperInvariant(),
                Colors: colorsArr is { Length: > 0 } ? colorsArr : null,
                Rarity: string.IsNullOrWhiteSpace(q.Rarity) ? null : q.Rarity.ToLowerInvariant(),
                Type: string.IsNullOrWhiteSpace(q.Type) ? null : q.Type.Trim(),
                Cmc: q.Cmc,
                CmcMin: q.CmcMin,
                CmcMax: q.CmcMax,
                Power: string.IsNullOrWhiteSpace(q.Power) ? null : q.Power.Trim(),
                Toughness: string.IsNullOrWhiteSpace(q.Toughness) ? null : q.Toughness.Trim(),
                Sort: sortKey,
                Ascending: asc,
                Take: Math.Clamp(q.Take ?? DefaultLimit, 1, MaxLimit),
                Skip: Math.Max(q.Skip ?? 0, 0));
        }

        private static SortKey ParseSort(string? sort, bool hasQuery) => sort?.ToLowerInvariant() switch
        {
            "name" => SortKey.Name,
            "cmc" => SortKey.Cmc,
            "releasedat" or "released" => SortKey.ReleasedAt,
            "rarity" => SortKey.Rarity,
            "relevance" => SortKey.Relevance,
            null or "" => hasQuery ? SortKey.Relevance : SortKey.Name,
            _ => SortKey.Name,
        };
    }

    private enum SortKey
    {
        Name,
        Cmc,
        ReleasedAt,
        Rarity,
        Relevance,
    }

    public async Task<CardResponse?> GetCardAsync(string oracleId, CancellationToken ct)
    {
        var row = await QueryRepresentativeAsync(oracleId, ct);
        if (row is null)
        {
            return null;
        }

        var response = await BuildCardResponseAsync(row, ct);
        return response;
    }

    public async Task<CardPrintingListResponse?> ListPrintingsAsync(string oracleId, CancellationToken ct)
    {
        var printings = await _db.CardPrintings
            .AsNoTracking()
            .Where(p => p.OracleId == oracleId)
            .ToListAsync(ct);

        if (printings.Count == 0)
        {
            return null;
        }

        var setCodes = printings.Select(p => p.SetCode).Distinct().ToList();
        var sets = await _db.Sets
            .AsNoTracking()
            .Where(s => setCodes.Contains(s.Code))
            .ToDictionaryAsync(s => s.Code, s => new { s.Name, s.ReleasedAt }, ct);

        // Newest set first, then setCode/collectorNumber for stable ordering inside a set.
        printings.Sort((a, b) =>
        {
            var aReleased = sets.TryGetValue(a.SetCode, out var aSet) ? aSet.ReleasedAt : null;
            var bReleased = sets.TryGetValue(b.SetCode, out var bSet) ? bSet.ReleasedAt : null;
            var byDate = Nullable.Compare(bReleased, aReleased);
            if (byDate != 0) return byDate;
            var bySet = string.CompareOrdinal(a.SetCode, b.SetCode);
            if (bySet != 0) return bySet;
            return string.CompareOrdinal(a.CollectorNumber, b.CollectorNumber);
        });

        var prices = await _prices.GetAsync(printings.Select(p => p.Id), ct);

        var results = new List<CardPrintingResponse>(printings.Count);
        foreach (var printing in printings)
        {
            var setName = sets.TryGetValue(printing.SetCode, out var s) ? s.Name : printing.SetCode;
            results.Add(await _mapper.MapAsync(printing, setName, prices.GetValueOrDefault(printing.Id), ct));
        }

        return new CardPrintingListResponse { Results = results };
    }

    public async Task<CardPrintingResponse?> GetPrintingAsync(
        string oracleId,
        string printingId,
        CancellationToken ct)
    {
        var printing = await _db.CardPrintings
            .AsNoTracking()
            .FirstOrDefaultAsync(p => p.Id == printingId, ct);

        if (printing is null || printing.OracleId != oracleId)
        {
            return null;
        }

        var setName = await _db.Sets
            .AsNoTracking()
            .Where(s => s.Code == printing.SetCode)
            .Select(s => s.Name)
            .FirstOrDefaultAsync(ct) ?? printing.SetCode;

        var prices = await _prices.GetAsync(new[] { printing.Id }, ct);
        var response = await _mapper.MapAsync(printing, setName, prices.GetValueOrDefault(printing.Id), ct);
        return response;
    }

    private async Task<CardResponse> BuildCardResponseAsync(RepresentativeRow row, CancellationToken ct)
    {
        var printing = new CardPrinting
        {
            Id = row.Id,
            OracleId = row.OracleId,
            Name = row.Name,
            SetCode = row.SetCode,
            CollectorNumber = row.CollectorNumber,
            ColorIdentity = row.ColorIdentity,
            Rarity = row.Rarity,
            ImageObjectKey = row.ImageObjectKey,
            ImageArtCropKey = row.ImageArtCropKey,
            Lang = row.Lang,
            Layout = row.Layout,
        };

        var images = await _mapper.MapImagesAsync(printing, ct);
        var faces = await _mapper.MapFacesAsync(row.Faces, ct);
        var typeLine = ComposeTypeLine(row.Supertype, row.Type, row.Subtype);

        return new CardResponse
        {
            OracleId = row.OracleId,
            Name = row.Name,
            TypeLine = typeLine,
            OracleText = row.OracleText,
            ColorIdentity = row.ColorIdentity,
            ManaCost = row.ManaCost,
            Cmc = row.Cmc,
            Power = row.Power,
            Toughness = row.Toughness,
            Layout = row.Layout,
            Thumbnail = HasImage(images) ? images : null,
            PrintingCount = row.PrintingCount,
            Faces = faces,
        };
    }

    private static bool HasImage(CardImageUrls images) =>
        !string.IsNullOrEmpty(images.Normal) || !string.IsNullOrEmpty(images.ArtCrop);

    private static string ComposeTypeLine(string? supertype, string type, string? subtype)
    {
        var parts = new List<string>(2);
        var head = string.IsNullOrWhiteSpace(supertype) ? type : $"{supertype} {type}";
        parts.Add(head);
        if (!string.IsNullOrWhiteSpace(subtype))
        {
            parts.Add(subtype);
        }

        return string.Join(" — ", parts);
    }

    // Allow-listed ORDER BY clause fragments. Built from CardListFilters.Sort + Ascending,
    // never built from raw user input — we never interpolate user strings into SQL.
    private static string OrderByClauseFor(CardListFilters f)
    {
        var dir = f.Ascending ? "ASC" : "DESC";
        return f.Sort switch
        {
            SortKey.Relevance => "CASE WHEN @q::text IS NULL THEN 0::real ELSE -similarity(r.\"Name\", @q) END ASC, r.\"Name\" ASC",
            SortKey.Cmc => $"r.\"Cmc\" {dir} NULLS LAST, r.\"Name\" ASC",
            SortKey.ReleasedAt => $"rep_set.\"ReleasedAt\" {dir} NULLS LAST, r.\"Name\" ASC",
            // Raw alphabetic on rarity is "common, mythic, rare, special, uncommon" which is
            // useless. Order by a hand-rolled rank instead.
            SortKey.Rarity => $"CASE r.\"Rarity\" WHEN 'common' THEN 1 WHEN 'uncommon' THEN 2 WHEN 'rare' THEN 3 WHEN 'mythic' THEN 4 WHEN 'special' THEN 5 ELSE 6 END {dir}, r.\"Name\" ASC",
            _ => $"r.\"Name\" {dir}",
        };
    }

    private async Task<List<RepresentativeRow>> QueryRepresentativesAsync(
        CardListFilters f,
        CancellationToken ct)
    {
        var sql = $$"""
            WITH filtered AS (
              SELECT p.*
              FROM cards.card_printings p
              WHERE (@q::text IS NULL OR similarity(p."Name", @q) > 0.2)
                AND (@set::text IS NULL OR p."SetCode" = @set)
                AND (@color::text IS NULL OR @color = ANY(p."ColorIdentity"))
                AND (@colors::text[] IS NULL OR p."ColorIdentity" @> @colors)
                AND (@rarity::text IS NULL OR p."Rarity" = @rarity)
                AND (@type::text IS NULL OR similarity(p."TypeLineFull", @type) > 0.2)
                AND (@cmc::real IS NULL OR p."Cmc" = @cmc)
                AND (@cmcMin::real IS NULL OR p."Cmc" >= @cmcMin)
                AND (@cmcMax::real IS NULL OR p."Cmc" <= @cmcMax)
                AND (@power::text IS NULL OR p."Power" = @power)
                AND (@toughness::text IS NULL OR p."Toughness" = @toughness)
            ),
            representatives AS (
              SELECT DISTINCT ON (f."OracleId")
                f.*,
                COUNT(*) OVER (PARTITION BY f."OracleId") AS printing_count
              FROM filtered f
              LEFT JOIN cards.sets s ON s."Code" = f."SetCode"
              ORDER BY f."OracleId",
                       (f."Lang" = 'en') DESC,
                       (f."ImageObjectKey" IS NOT NULL) DESC,
                       s."ReleasedAt" DESC NULLS LAST,
                       f."IsFoil" ASC,
                       f."Id" ASC
            )
            SELECT
              r."Id",
              r."OracleId",
              r."Name",
              r."SetCode",
              r."CollectorNumber",
              r."ColorIdentity",
              r."Rarity",
              r."ImageObjectKey",
              r."ImageArtCropKey",
              r."Supertype",
              r."Type",
              r."Subtype",
              r."OracleText",
              r."Power",
              r."Toughness",
              r."ManaCost",
              r."Cmc",
              r."Lang",
              r."Layout",
              r."Faces",
              r.printing_count AS "PrintingCount"
            FROM representatives r
            LEFT JOIN cards.sets rep_set ON rep_set."Code" = r."SetCode"
            ORDER BY {{OrderByClauseFor(f)}}
            LIMIT @take OFFSET @skip;
            """;

        var rows = new List<RepresentativeRow>();
        var conn = (NpgsqlConnection) _db.Database.GetDbConnection();
        var opened = conn.State != System.Data.ConnectionState.Open;
        if (opened)
        {
            await conn.OpenAsync(ct);
        }

        try
        {
            await using var cmd = new NpgsqlCommand(sql, conn);
            BindFilterParams(cmd, f);
            cmd.Parameters.AddWithValue("take", f.Take);
            cmd.Parameters.AddWithValue("skip", f.Skip);

            await using var reader = await cmd.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct))
            {
                rows.Add(ReadRepresentative(reader));
            }
        }
        finally
        {
            if (opened)
            {
                await conn.CloseAsync();
            }
        }

        return rows;
    }

    private static void BindFilterParams(NpgsqlCommand cmd, CardListFilters f)
    {
        AddNullableText(cmd, "q", f.Q);
        AddNullableText(cmd, "set", f.Set);
        AddNullableText(cmd, "color", f.Color);
        AddNullableText(cmd, "rarity", f.Rarity);
        AddNullableText(cmd, "type", f.Type);
        AddNullableText(cmd, "power", f.Power);
        AddNullableText(cmd, "toughness", f.Toughness);
        AddNullableReal(cmd, "cmc", f.Cmc);
        AddNullableReal(cmd, "cmcMin", f.CmcMin);
        AddNullableReal(cmd, "cmcMax", f.CmcMax);

        var colorsParam = cmd.Parameters.Add("colors", NpgsqlTypes.NpgsqlDbType.Array | NpgsqlTypes.NpgsqlDbType.Text);
        colorsParam.Value = (object?) f.Colors ?? DBNull.Value;
    }

    private static void AddNullableReal(NpgsqlCommand cmd, string name, float? value)
    {
        var p = cmd.Parameters.Add(name, NpgsqlTypes.NpgsqlDbType.Real);
        p.Value = value.HasValue ? value.Value : DBNull.Value;
    }

    private async Task<RepresentativeRow?> QueryRepresentativeAsync(string oracleId, CancellationToken ct)
    {
        const string sql = """
            SELECT DISTINCT ON (p."OracleId")
              p."Id",
              p."OracleId",
              p."Name",
              p."SetCode",
              p."CollectorNumber",
              p."ColorIdentity",
              p."Rarity",
              p."ImageObjectKey",
              p."ImageArtCropKey",
              p."Supertype",
              p."Type",
              p."Subtype",
              p."OracleText",
              p."Power",
              p."Toughness",
              p."ManaCost",
              p."Cmc",
              p."Lang",
              p."Layout",
              p."Faces",
              COUNT(*) OVER (PARTITION BY p."OracleId")::int AS "PrintingCount"
            FROM cards.card_printings p
            LEFT JOIN cards.sets s ON s."Code" = p."SetCode"
            WHERE p."OracleId" = @oracleId
            ORDER BY p."OracleId",
                     (p."Lang" = 'en') DESC,
                     (p."ImageObjectKey" IS NOT NULL) DESC,
                     s."ReleasedAt" DESC NULLS LAST,
                     p."IsFoil" ASC,
                     p."Id" ASC
            LIMIT 1;
            """;

        var conn = (NpgsqlConnection) _db.Database.GetDbConnection();
        var opened = conn.State != System.Data.ConnectionState.Open;
        if (opened)
        {
            await conn.OpenAsync(ct);
        }

        try
        {
            await using var cmd = new NpgsqlCommand(sql, conn);
            cmd.Parameters.AddWithValue("oracleId", oracleId);

            await using var reader = await cmd.ExecuteReaderAsync(ct);
            if (!await reader.ReadAsync(ct))
            {
                return null;
            }

            return ReadRepresentative(reader);
        }
        finally
        {
            if (opened)
            {
                await conn.CloseAsync();
            }
        }
    }

    private async Task<int> CountOraclesAsync(
        CardListFilters f,
        CancellationToken ct)
    {
        const string sql = """
            SELECT COUNT(DISTINCT p."OracleId")::int
            FROM cards.card_printings p
            WHERE (@q::text IS NULL OR similarity(p."Name", @q) > 0.2)
              AND (@set::text IS NULL OR p."SetCode" = @set)
              AND (@color::text IS NULL OR @color = ANY(p."ColorIdentity"))
              AND (@colors::text[] IS NULL OR p."ColorIdentity" @> @colors)
              AND (@rarity::text IS NULL OR p."Rarity" = @rarity)
              AND (@type::text IS NULL OR similarity(p."TypeLineFull", @type) > 0.2)
              AND (@cmc::real IS NULL OR p."Cmc" = @cmc)
              AND (@cmcMin::real IS NULL OR p."Cmc" >= @cmcMin)
              AND (@cmcMax::real IS NULL OR p."Cmc" <= @cmcMax)
              AND (@power::text IS NULL OR p."Power" = @power)
              AND (@toughness::text IS NULL OR p."Toughness" = @toughness);
            """;

        var conn = (NpgsqlConnection) _db.Database.GetDbConnection();
        var opened = conn.State != System.Data.ConnectionState.Open;
        if (opened)
        {
            await conn.OpenAsync(ct);
        }

        try
        {
            await using var cmd = new NpgsqlCommand(sql, conn);
            BindFilterParams(cmd, f);

            var scalar = await cmd.ExecuteScalarAsync(ct);
            return scalar is int i ? i : 0;
        }
        finally
        {
            if (opened)
            {
                await conn.CloseAsync();
            }
        }
    }

    private static void AddNullableText(NpgsqlCommand cmd, string name, string? value)
    {
        var p = cmd.Parameters.Add(name, NpgsqlTypes.NpgsqlDbType.Text);
        p.Value = (object?) value ?? DBNull.Value;
    }

    private static RepresentativeRow ReadRepresentative(NpgsqlDataReader reader) => new()
    {
        Id = reader.GetString(reader.GetOrdinal("Id")),
        OracleId = reader.GetString(reader.GetOrdinal("OracleId")),
        Name = reader.GetString(reader.GetOrdinal("Name")),
        SetCode = reader.GetString(reader.GetOrdinal("SetCode")),
        CollectorNumber = reader.GetString(reader.GetOrdinal("CollectorNumber")),
        ColorIdentity = (string[]) reader.GetValue(reader.GetOrdinal("ColorIdentity")),
        Rarity = reader.GetString(reader.GetOrdinal("Rarity")),
        ImageObjectKey = ReadNullable(reader, "ImageObjectKey"),
        ImageArtCropKey = ReadNullable(reader, "ImageArtCropKey"),
        Supertype = ReadNullable(reader, "Supertype"),
        Type = reader.GetString(reader.GetOrdinal("Type")),
        Subtype = ReadNullable(reader, "Subtype"),
        OracleText = ReadNullable(reader, "OracleText"),
        Power = ReadNullable(reader, "Power"),
        Toughness = ReadNullable(reader, "Toughness"),
        Lang = reader.GetString(reader.GetOrdinal("Lang")),
        Layout = reader.GetString(reader.GetOrdinal("Layout")),
        ManaCost = ReadNullable(reader, "ManaCost"),
        Cmc = ReadNullableReal(reader, "Cmc"),
        Faces = ReadFaces(reader),
        PrintingCount = reader.GetInt32(reader.GetOrdinal("PrintingCount")),
    };

    private static float? ReadNullableReal(NpgsqlDataReader reader, string column)
    {
        var ordinal = reader.GetOrdinal(column);
        return reader.IsDBNull(ordinal) ? null : reader.GetFloat(ordinal);
    }

    private static List<CardFace>? ReadFaces(NpgsqlDataReader reader)
    {
        var ord = reader.GetOrdinal("Faces");
        if (reader.IsDBNull(ord))
        {
            return null;
        }
        // Npgsql is configured with EnableDynamicJson() in Program.cs, so jsonb columns
        // can be deserialized directly to a typed CLR list.
        return reader.GetFieldValue<List<CardFace>>(ord);
    }

    private static string? ReadNullable(NpgsqlDataReader reader, string column)
    {
        var ordinal = reader.GetOrdinal(column);
        return reader.IsDBNull(ordinal) ? null : reader.GetString(ordinal);
    }

    private sealed class RepresentativeRow
    {
        public required string Id { get; set; }

        public required string OracleId { get; set; }

        public required string Name { get; set; }

        public required string SetCode { get; set; }

        public required string CollectorNumber { get; set; }

        public required string[] ColorIdentity { get; set; }

        public required string Rarity { get; set; }

        public string? ImageObjectKey { get; set; }

        public string? ImageArtCropKey { get; set; }

        public string? Supertype { get; set; }

        public required string Type { get; set; }

        public string? Subtype { get; set; }

        public string? OracleText { get; set; }

        public string? Power { get; set; }

        public string? Toughness { get; set; }

        public required string Lang { get; set; }

        public required string Layout { get; set; }

        public string? ManaCost { get; set; }

        public float? Cmc { get; set; }

        public List<CardFace>? Faces { get; set; }

        public int PrintingCount { get; set; }
    }
}
