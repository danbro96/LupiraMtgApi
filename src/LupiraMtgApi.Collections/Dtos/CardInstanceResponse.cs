using LupiraMtgApi.Catalog.Dtos.Cards;

namespace LupiraMtgApi.Collections.Dtos;

public sealed class CardInstanceResponse
{
    public required Guid InstanceId { get; set; }

    public required CardPrintingResponse Printing { get; set; }

    public required bool IsFoil { get; set; }

    public required string Language { get; set; }

    public required string Condition { get; set; }

    public required DateTimeOffset AcquiredAt { get; set; }

    public required Guid? CollectionId { get; set; }

    public required string? CollectionName { get; set; }
}
