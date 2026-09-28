using LupiraMtgApi.Recognition.Domain;
using Xunit;

namespace LupiraMtgApi.UnitTests.Recognition;

public class PHashOrientationTests
{
    // Pairs from on-device scans of one card: upright crops (4–8 upright) vs upside-down crops (14+ upright).
    [Theory]
    [InlineData(4, 16, false)]
    [InlineData(8, 16, false)]
    [InlineData(6, 12, false)]
    [InlineData(16, 10, true)]
    [InlineData(14, 4, true)]
    [InlineData(14, 12, true)]
    [InlineData(int.MaxValue, 10, true)]
    [InlineData(12, 11, false)]
    [InlineData(16, int.MaxValue, false)]
    [InlineData(int.MaxValue, int.MaxValue, false)]
    public void Flips_when_upright_matches_poorly_and_flipped_matches_better(int upright, int flipped, bool expected)
    {
        Assert.Equal(expected, PHashOrientation.ShouldFlip(upright, flipped, uprightMinDistance: 12, minMargin: 2));
    }
}
