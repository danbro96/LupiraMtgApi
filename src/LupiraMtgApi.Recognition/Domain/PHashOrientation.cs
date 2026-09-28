namespace LupiraMtgApi.Recognition.Domain;

public static class PHashOrientation
{
    /// <summary>
    /// True when the crop is most likely upside down: the upright side matches the catalogue poorly and the flipped
    /// side matches better. The upright distance is the discriminating signal — on device, upright crops matched at
    /// 4–8 bits and upside-down ones at 14+ (or not at all), while the flipped side's absolute distance overlapped
    /// with noise. Pass <see cref="int.MaxValue"/> for "no hit".
    /// </summary>
    public static bool ShouldFlip(int uprightBest, int flippedBest, int uprightMinDistance, int minMargin)
    {
        if (uprightBest < uprightMinDistance || flippedBest == int.MaxValue)
        {
            return false;
        }

        return uprightBest == int.MaxValue || uprightBest - flippedBest >= minMargin;
    }
}
