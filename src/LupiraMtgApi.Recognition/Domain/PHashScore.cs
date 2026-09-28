namespace LupiraMtgApi.Recognition.Domain;

public static class PHashScore
{
    /// <summary>1.0 at or below <paramref name="fullScoreDistance"/>, 0 at or above <paramref name="zeroScoreDistance"/>, linear between.</summary>
    public static double FromDistance(int distance, int fullScoreDistance, int zeroScoreDistance)
    {
        if (distance <= fullScoreDistance)
        {
            return 1.0;
        }

        if (distance >= zeroScoreDistance)
        {
            return 0.0;
        }

        return (double)(zeroScoreDistance - distance) / (zeroScoreDistance - fullScoreDistance);
    }
}
