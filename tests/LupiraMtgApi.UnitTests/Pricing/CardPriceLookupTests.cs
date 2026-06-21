using LupiraMtgApi.Pricing.Application;
using LupiraMtgApi.Pricing.Domain;
using Xunit;

namespace LupiraMtgApi.UnitTests.Pricing;

public class CardPriceLookupTests
{
    [Fact]
    public async Task Maps_seeded_rows_to_responses_and_omits_unknown_ids()
    {
        using var test = new PricingTestDb();
        await using (var seed = test.Create())
        {
            seed.CardPricesLatest.Add(new CardPriceLatest
            {
                PrintingId = "p1",
                Eur = 1.50m,
                EurFoil = 4.00m,
                UpdatedAt = new DateTimeOffset(2026, 6, 21, 0, 0, 0, TimeSpan.Zero),
            });
            await seed.SaveChangesAsync();
        }

        await using var db = test.Create();
        var result = await new CardPriceLookup(db).GetAsync(new[] { "p1", "missing" }, default);

        Assert.True(result.ContainsKey("p1"));
        Assert.Equal(1.50m, result["p1"].Eur);
        Assert.Equal(4.00m, result["p1"].EurFoil);
        Assert.False(result.ContainsKey("missing"));
    }

    [Fact]
    public async Task Empty_id_set_returns_empty_without_querying()
    {
        using var test = new PricingTestDb();
        await using var db = test.Create();

        var result = await new CardPriceLookup(db).GetAsync(Array.Empty<string>(), default);

        Assert.Empty(result);
    }
}
