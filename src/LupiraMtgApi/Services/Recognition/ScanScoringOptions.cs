namespace LupiraMtgApi.Services.Recognition;

public sealed class ScanScoringOptions
{
    public double PHashWeight { get; set; } = 0.45;

    public double OcrWeight { get; set; } = 0.55;

    public double NameWeight { get; set; } = 0.40;

    public double TypeLineWeight { get; set; } = 0.10;

    public double RulesTextWeight { get; set; } = 0.20;

    public double PowerToughnessWeight { get; set; } = 0.10;

    public double BottomMetadataWeight { get; set; } = 0.20;

    public double NameCutoff { get; set; } = 0.30;

    public double TypeLineCutoff { get; set; } = 0.40;

    public double RulesTextCutoff { get; set; } = 0.30;

    public int NameTopK { get; set; } = 25;

    public int TypeLineTopK { get; set; } = 50;

    public int RulesTextTopK { get; set; } = 50;

    public double HighCombined { get; set; } = 0.85;

    public double MediumCombined { get; set; } = 0.60;

    public double HighZoneAgreementMinScore { get; set; } = 0.70;

    public int HighZoneAgreementMinCount { get; set; } = 2;

    // Hamming-distance cutoff for the full-card pHash BK-tree. Wider than the art-pHash
    // cutoff because the full-card hash is more sensitive to lighting/foil/exposure
    // shifts on the frame; matching needs a bigger tolerance window. Empirically set to
    // 16 in initial deploy; tune from telemetry on the `phash.full_best_hamming` span tag.
    public int FullCardPHashMaxHamming { get; set; } = 16;

    // ---- Pipeline structural limits (round-4 refactor moved these out of ScanHandler consts) ----

    public int MaxImageBytes { get; set; } = 4 * 1024 * 1024;

    public int PHashTopK { get; set; } = 10;

    public int FinalTopN { get; set; } = 5;

    public int PHashMaxHamming { get; set; } = 12;

    /// <summary>If the cropper rotated to portrait but the first pass populates fewer than this many zones, run the 180° rotation retry.</summary>
    public int RotationRetryCoverageThreshold { get; set; } = 3;

    /// <summary>Skip the retry entirely when the first pass populates this many zones — it almost never wins from this state.</summary>
    public int RotationRetryHighCoverageSkipThreshold { get; set; } = 4;

    /// <summary>Per-zone score floor counting toward "strong agreement" on the borderline retry-skip path.</summary>
    public double RotationRetryStrongZoneScoreThreshold { get; set; } = 0.7;

    public int RotationRetryStrongZoneMinCount { get; set; } = 3;

    /// <summary>Used when a printing's set has no matching set_type_weights row. Neutral midpoint.</summary>
    public double DefaultSetTypeWeight { get; set; } = 0.5;
}
