namespace LupiraMtgApi.Recognition.Application.Pipeline;

/// <summary>
/// Per-printing accumulator carried through the scoring/hydration steps.
/// Replaces the private FinalRow type that lived inside ScanHandler. Combines
/// pHash signal, per-zone OCR scores, set-type weight, and the final fused score
/// in one place; each step adds the slice it owns.
/// </summary>
public sealed class RankedCandidate
{
    public required string PrintingId { get; init; }

    public PrintingZoneScores? ZoneScores { get; set; }

    public double HammingScore { get; set; }

    public int? HammingDistance { get; set; }

    public string SetCode { get; set; } = string.Empty;

    public string? SetType { get; set; }

    public double SetTypeWeight { get; set; } = 0.5;

    public double FinalScore { get; set; }
}
