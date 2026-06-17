namespace LupiraMtgApi.Recognition.Domain;

public sealed class CardZones
{
    public required string Name { get; set; }

    public required string TypeLine { get; set; }

    public required string RulesText { get; set; }

    public required string PowerToughness { get; set; }

    public required string BottomMetadata { get; set; }

    /// <summary>Mean OCR confidence of the region(s) classified into <see cref="Name"/>; 0 when empty.</summary>
    public double NameConfidence { get; set; }

    public double TypeLineConfidence { get; set; }

    public double RulesTextConfidence { get; set; }

    public double PowerToughnessConfidence { get; set; }

    public double BottomMetadataConfidence { get; set; }

    public static CardZones Empty { get; } = new()
    {
        Name = string.Empty,
        TypeLine = string.Empty,
        RulesText = string.Empty,
        PowerToughness = string.Empty,
        BottomMetadata = string.Empty,
    };

    public bool IsEmpty => string.IsNullOrWhiteSpace(this.Name)
        && string.IsNullOrWhiteSpace(this.TypeLine)
        && string.IsNullOrWhiteSpace(this.RulesText)
        && string.IsNullOrWhiteSpace(this.PowerToughness)
        && string.IsNullOrWhiteSpace(this.BottomMetadata);
}
