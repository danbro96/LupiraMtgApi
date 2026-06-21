using LupiraMtgApi.Pricing.Application;
using LupiraMtgApi.Pricing.Domain;
using Microsoft.Extensions.Options;
using Xunit;

namespace LupiraMtgApi.Tests.Pricing;

public class PriceHistoryServiceTests
{
    private static PriceHistoryService Service(LupiraMtgApi.Pricing.Data.PricingDbContext db, int retentionDays = 365) =>
        new(db, Options.Create(new LupiraMtgApi.Pricing.PricingOptions { HistoryRetentionDays = retentionDays }));

    [Fact]
    public async Task Returns_null_when_no_points_recorded()
    {
        using var test = new PricingTestDb();
        await using var db = test.Create();

        var result = await Service(db).GetAsync("p1", null, null, default);

        Assert.Null(result);
    }

    [Fact]
    public async Task Returns_points_oldest_first()
    {
        using var test = new PricingTestDb();
        await using (var seed = test.Create())
        {
            seed.CardPricePoints.Add(new CardPricePoint { PrintingId = "p1", ObservedOn = new DateOnly(2026, 6, 21), Eur = 2.00m, Source = "test" });
            seed.CardPricePoints.Add(new CardPricePoint { PrintingId = "p1", ObservedOn = new DateOnly(2026, 6, 19), Eur = 1.50m, Source = "test" });
            await seed.SaveChangesAsync();
        }

        await using var db = test.Create();
        var result = await Service(db).GetAsync("p1", null, null, default);

        Assert.NotNull(result);
        Assert.Equal(2, result!.Points.Count);
        Assert.Equal(new DateOnly(2026, 6, 19), result.Points[0].ObservedOn);
        Assert.Equal(new DateOnly(2026, 6, 21), result.Points[1].ObservedOn);
    }
}
