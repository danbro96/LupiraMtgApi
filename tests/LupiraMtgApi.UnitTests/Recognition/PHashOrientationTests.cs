using LupiraMtgApi.Recognition.Domain;
using Xunit;

namespace LupiraMtgApi.UnitTests.Recognition;

public class PHashOrientationTests
{
    [Theory]
    [InlineData(12, 2, true)]
    [InlineData(int.MaxValue, 4, true)]
    [InlineData(7, 4, false)]
    [InlineData(10, 6, false)]
    [InlineData(3, 1, false)]
    [InlineData(int.MaxValue, int.MaxValue, false)]
    public void Flips_only_on_a_close_flipped_match_with_a_clear_margin(int upright, int flipped, bool expected)
    {
        Assert.Equal(expected, PHashOrientation.ShouldFlip(upright, flipped, maxDistance: 4, minMargin: 4));
    }
}
