namespace LupiraMtgApi.Services.Recognition;

public sealed class CardZones
{
    public required string Name { get; set; }

    public required string TypeLine { get; set; }

    public required string RulesText { get; set; }

    public required string PowerToughness { get; set; }

    public required string BottomMetadata { get; set; }

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