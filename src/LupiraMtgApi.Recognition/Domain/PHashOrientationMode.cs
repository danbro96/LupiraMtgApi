namespace LupiraMtgApi.Recognition.Domain;

/// <summary>How the pre-OCR pHash orientation probe is used.</summary>
public enum PHashOrientationMode
{
    Off,

    /// <summary>Probe in parallel with OCR and record the verdict on the trace only.</summary>
    Shadow,

    /// <summary>Probe before OCR and rotate the crop 180° when the flipped side wins decisively.</summary>
    Flip,
}
