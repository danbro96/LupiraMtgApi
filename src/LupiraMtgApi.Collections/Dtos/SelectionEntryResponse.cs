using LupiraMtgApi.Catalog.Dtos.Cards;

namespace LupiraMtgApi.Collections.Dtos;

public sealed class SelectionEntryResponse
{
    public required Guid InstanceId { get; set; }

    public required CardPrintingResponse Printing { get; set; }

    public required bool IsFoil { get; set; }

    public required string Language { get; set; }

    public required string Condition { get; set; }

    public required double Confidence { get; set; }
}
