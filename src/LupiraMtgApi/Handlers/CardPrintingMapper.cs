using LupiraMtgApi.Data.Entities;
using LupiraMtgApi.Models;
using LupiraMtgApi.Services;
using LupiraMtgApi.Models.Cards;
using LupiraMtgApi.Services.Storage;
namespace LupiraMtgApi.Handlers;

public sealed class CardPrintingMapper
{
    private static readonly TimeSpan PresignExpiry = TimeSpan.FromMinutes(15);

    private readonly IImageStore _images;

    public CardPrintingMapper(IImageStore images)
    {
        _images = images;
    }

    public async Task<CardPrintingResponse> MapAsync(CardPrinting printing, string setName, CancellationToken ct)
    {
        var imageUrls = new CardImageUrls();

        if (printing.ImageObjectKey is { Length: > 0 })
        {
            imageUrls.Normal = await _images.CreatePresignedGetUrlAsync(printing.ImageObjectKey, PresignExpiry, ct);
        }

        if (printing.ImageArtCropKey is { Length: > 0 })
        {
            imageUrls.ArtCrop = await _images.CreatePresignedGetUrlAsync(printing.ImageArtCropKey, PresignExpiry, ct);
        }

        return new CardPrintingResponse
        {
            Id = printing.Id,
            OracleId = printing.OracleId,
            Name = printing.Name,
            SetCode = printing.SetCode,
            SetName = setName,
            CollectorNumber = printing.CollectorNumber,
            ColorIdentity = printing.ColorIdentity,
            Rarity = printing.Rarity,
            Images = imageUrls,
            Prices = printing.Prices,
        };
    }
}
