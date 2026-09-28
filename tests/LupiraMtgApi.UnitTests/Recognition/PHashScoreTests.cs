using LupiraMtgApi.Recognition.Domain;
using Xunit;

namespace LupiraMtgApi.UnitTests.Recognition;

public class PHashScoreTests
{
    [Theory]
    [InlineData(0, 1.0)]
    [InlineData(2, 1.0)]
    [InlineData(7, 0.5)]
    [InlineData(8, 0.4)]
    [InlineData(12, 0.0)]
    [InlineData(16, 0.0)]
    public void Linear_between_full_and_zero_distance(int distance, double expected)
    {
        Assert.Equal(expected, PHashScore.FromDistance(distance, fullScoreDistance: 2, zeroScoreDistance: 12), precision: 6);
    }
}
