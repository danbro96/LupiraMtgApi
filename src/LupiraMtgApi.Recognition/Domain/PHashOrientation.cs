namespace LupiraMtgApi.Recognition.Domain;

public static class PHashOrientation
{
    /// <summary>
    /// True when the 180°-rotated crop matches the catalogue decisively better than the upright one. Both
    /// conditions matter: unrelated cards land within 6–8 bits of a phone capture, so only a close flipped match
    /// with a clear margin over the upright side is trustworthy. Pass <see cref="int.MaxValue"/> for "no hit".
    /// </summary>
    public static bool ShouldFlip(int uprightBest, int flippedBest, int maxDistance, int minMargin)
    {
        if (flippedBest > maxDistance)
        {
            return false;
        }

        return uprightBest == int.MaxValue || uprightBest - flippedBest >= minMargin;
    }
}
