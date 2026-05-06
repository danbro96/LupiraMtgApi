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

    /// <summary>
    /// Minimum OCR confidence (mean per-token probability) required from the strongest
    /// contributing zone before a scan can be classified as High. Guards against the
    /// case where Florence returns plausible text from a misread region — the trigram
    /// match might land on a real card but the underlying OCR is junk. Set to 0 to
    /// disable the gate entirely.
    /// </summary>
    public double HighZoneConfidenceMinScore { get; set; } = 0.60;

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

    /// <summary>
    /// Smoothing floor applied when blending OCR per-region confidence into zone aggregation:
    /// the effective zone weight is <c>BaseWeight * (Floor + (1 - Floor) * ZoneConfidence)</c>,
    /// so a zone with <c>Confidence = 0</c> still contributes <c>Floor</c> of its base weight
    /// rather than vanishing. Keeps noisy reads from being silently dropped while still letting
    /// confident reads outweigh them.
    /// </summary>
    public double OcrConfidenceFloor { get; set; } = 0.5;

    /// <summary>Used when a printing's set has no matching set_type_weights row. Neutral midpoint.</summary>
    public double DefaultSetTypeWeight { get; set; } = 0.5;
}
