using System.Diagnostics;
using LupiraMtgApi.Recognition.Application.Pipeline;
using LupiraMtgApi.Recognition.Application.Steps;
using LupiraMtgApi.Recognition.Domain;
using LupiraMtgApi.Recognition.Infrastructure.Imaging;
using Microsoft.Extensions.Options;
using Xunit;

namespace LupiraMtgApi.UnitTests.Recognition;

public class FusionStepTests
{
    [Fact]
    public async Task Ocr_matched_card_outranks_pHash_only_noise_at_typical_phone_distance()
    {
        var ctx = new ScanContext
        {
            ScanId = Guid.NewGuid(),
            ScannedAt = DateTimeOffset.UtcNow,
            OriginalBytes = [],
            MediaType = "image/jpeg",
            ScanStopwatch = Stopwatch.StartNew(),
            ZoneScoring = new CardZoneScoringResult
            {
                ByPrinting = new Dictionary<string, PrintingZoneScores>
                {
                    ["right"] = new() { PrintingId = "right", AggregateScore = 0.58 },
                },
                Weights = new ZoneWeights(),
            },
            PHashHits = [new PHashIndex.PHashHit("noise", 8)],
        };

        var result = await new FusionStep(Options.Create(new ScanScoringOptions())).ExecuteAsync(ctx, CancellationToken.None);

        Assert.True(result.ByPrinting["right"].FinalScore > result.ByPrinting["noise"].FinalScore);
        Assert.Equal(0.4, result.ByPrinting["noise"].FinalScore, precision: 6);
    }
}
