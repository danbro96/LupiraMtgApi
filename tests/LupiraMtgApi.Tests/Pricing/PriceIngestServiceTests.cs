using LupiraMtgApi.Pricing.Application;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace LupiraMtgApi.Tests.Pricing;

/// <summary>
/// Covers the store-on-change contract of <see cref="PriceIngestService"/>: latest is always upserted,
/// but a history point is written only when the value actually moved.
/// </summary>
public class PriceIngestServiceTests
{
    private static readonly DateOnly Day1 = new(2026, 6, 20);
    private static readonly DateOnly Day2 = new(2026, 6, 21);

    private static async Task IngestAsync(PricingTestDb test, DateOnly on, params PriceObservation[] obs)
    {
        await using var db = test.Create();
        await new PriceIngestService(db).IngestBatchAsync(obs, on, "test", default);
    }

    [Fact]
    public async Task New_printing_upserts_latest_and_writes_initial_point()
    {
        using var test = new PricingTestDb();

        await IngestAsync(test, Day1, new PriceObservation { PrintingId = "p1", Eur = 1.50m, EurFoil = 3.00m });

        await using var assert = test.Create();
        var latest = await assert.CardPricesLatest.SingleAsync();
        Assert.Equal("p1", latest.PrintingId);
        Assert.Equal(1.50m, latest.Eur);
        Assert.Equal(3.00m, latest.EurFoil);
        Assert.Equal(1, await assert.CardPricePoints.CountAsync());
    }

    [Fact]
    public async Task Unchanged_value_next_day_writes_no_new_point()
    {
        using var test = new PricingTestDb();

        await IngestAsync(test, Day1, new PriceObservation { PrintingId = "p1", Eur = 1.50m });
        await IngestAsync(test, Day2, new PriceObservation { PrintingId = "p1", Eur = 1.50m });

        await using var assert = test.Create();
        Assert.Equal(1, await assert.CardPricePoints.CountAsync());
        Assert.Equal(Day1, (await assert.CardPricePoints.SingleAsync()).ObservedOn);
    }

    [Fact]
    public async Task Changed_value_next_day_writes_new_point_and_updates_latest()
    {
        using var test = new PricingTestDb();

        await IngestAsync(test, Day1, new PriceObservation { PrintingId = "p1", Eur = 1.50m });
        await IngestAsync(test, Day2, new PriceObservation { PrintingId = "p1", Eur = 2.00m });

        await using var assert = test.Create();
        Assert.Equal(2, await assert.CardPricePoints.CountAsync());
        Assert.Equal(2.00m, (await assert.CardPricesLatest.SingleAsync()).Eur);
    }

    [Fact]
    public async Task Same_day_rerun_with_same_value_is_a_no_op()
    {
        using var test = new PricingTestDb();

        await IngestAsync(test, Day1, new PriceObservation { PrintingId = "p1", Eur = 1.50m });
        await IngestAsync(test, Day1, new PriceObservation { PrintingId = "p1", Eur = 1.50m });

        await using var assert = test.Create();
        Assert.Equal(1, await assert.CardPricePoints.CountAsync());
    }

    [Fact]
    public async Task Same_day_changed_value_updates_the_existing_point_not_duplicate()
    {
        using var test = new PricingTestDb();

        await IngestAsync(test, Day1, new PriceObservation { PrintingId = "p1", Eur = 1.50m });
        await IngestAsync(test, Day1, new PriceObservation { PrintingId = "p1", Eur = 1.75m });

        await using var assert = test.Create();
        var point = await assert.CardPricePoints.SingleAsync();
        Assert.Equal(1.75m, point.Eur);
    }

    [Fact]
    public async Task Observation_with_no_values_is_skipped()
    {
        using var test = new PricingTestDb();

        await IngestAsync(test, Day1, new PriceObservation { PrintingId = "p1", Eur = null, EurFoil = null });

        await using var assert = test.Create();
        Assert.Equal(0, await assert.CardPricesLatest.CountAsync());
        Assert.Equal(0, await assert.CardPricePoints.CountAsync());
    }
}
