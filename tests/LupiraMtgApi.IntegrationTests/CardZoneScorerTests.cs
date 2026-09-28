using LupiraMtgApi.Catalog.Data;
using LupiraMtgApi.Catalog.Domain;
using LupiraMtgApi.Recognition.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace LupiraMtgApi.IntegrationTests;

/// <summary>Zone scoring against real pg_trgm, using OCR zones captured from phone scans of Entish Restoration (LTR #163).</summary>
[Collection("integration")]
public sealed class CardZoneScorerTests(MtgApiTestFactory factory) : IAsyncLifetime
{
    private const string Entish = "test-entish-ltr-163";
    private const string Meldweb = "test-meldweb-one-59";

    public async Task InitializeAsync()
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<LupiraMtgDbContext>();
        if (await db.CardPrintings.AnyAsync(p => p.Id == Entish))
        {
            return;
        }

        db.CardPrintings.Add(Printing(Entish, "Entish Restoration", "ltr", "163", "uncommon", "Instant", null));
        db.CardPrintings.Add(Printing(Meldweb, "Meldweb Curator", "one", "59", "common", "Artifact Creature", "Phyrexian Wizard"));
        for (var i = 0; i < 60; i++)
        {
            db.CardPrintings.Add(Printing($"test-instant-{i}", $"Filler Spell {i}", "tst", (200 + i).ToString(), "common", "Instant", null));
        }

        await db.SaveChangesAsync();
    }

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task Garbled_set_code_still_confirms_the_name_match_via_type_line_and_collector_number()
    {
        var result = await Score(Zones("U 0163 U 016EN SCARER MOORE"), seeds: [Meldweb]);

        var entish = result.ByPrinting[Entish];
        Assert.True(entish.NameScore > 0.3);
        Assert.Equal(1.0, entish.TypeLineScore, precision: 3);
        Assert.Equal(0.6, entish.BottomMetadataScore, precision: 3);
        Assert.True(entish.AggregateScore > 0.6);

        Assert.Equal(0.0, result.ByPrinting[Meldweb].AggregateScore, precision: 3);
        Assert.DoesNotContain(result.ByPrinting.Keys, id => id.StartsWith("test-instant-", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Readable_modern_collector_line_is_an_exact_match()
    {
        var result = await Score(Zones("U 0163 LTR . EN >CALDER MOORE"), seeds: []);

        var entish = result.ByPrinting[Entish];
        Assert.Equal(1.0, entish.BottomMetadataScore, precision: 3);
        Assert.True(entish.ContributingZoneCount(0.7) >= 2);
    }

    private async Task<CardZoneScoringResult> Score(CardZones zones, IEnumerable<string> seeds)
    {
        using var scope = factory.Services.CreateScope();
        var scorer = scope.ServiceProvider.GetRequiredService<CardZoneScorer>();
        return await scorer.ScoreAsync(zones, symbolMatch: null, seeds, CancellationToken.None);
    }

    private static CardZones Zones(string bottom) => new()
    {
        Name = "</s>English Restoration",
        TypeLine = "Instant",
        RulesText = string.Empty,
        PowerToughness = string.Empty,
        BottomMetadata = bottom,
        NameConfidence = 0.52,
        TypeLineConfidence = 0.66,
        BottomMetadataConfidence = 0.35,
    };

    private static CardPrinting Printing(string id, string name, string set, string number, string rarity, string type, string? subtype) => new()
    {
        Id = id,
        OracleId = id,
        Name = name,
        SetCode = set,
        CollectorNumber = number,
        ColorIdentity = [],
        Rarity = rarity,
        Type = type,
        Subtype = subtype,
        Lang = "en",
    };
}
