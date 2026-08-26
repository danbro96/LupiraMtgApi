using LupiraMtgApi.Catalog.Domain;
using LupiraMtgApi.Catalog.Dtos.Cards;
using LupiraMtgApi.Catalog.Infrastructure.Storage;
using LupiraMtgApi.Pricing.Dtos;

namespace LupiraMtgApi.Catalog.Mappers;

public sealed class CardPrintingMapper
{
    private static readonly TimeSpan PresignExpiry = TimeSpan.FromMinutes(15);

    private readonly IImageStore _images;

    public CardPrintingMapper(IImageStore images)
    {
        _images = images;
    }

    public async Task<CardImageUrls> MapImagesAsync(CardPrinting printing, CancellationToken ct) => new()
    {
        Normal = printing.ImageObjectKey is { Length: > 0 }
            ? await _images.CreatePresignedGetUrlAsync(printing.ImageObjectKey, PresignExpiry, ct)
            : null,
        ArtCrop = printing.ImageArtCropKey is { Length: > 0 }
            ? await _images.CreatePresignedGetUrlAsync(printing.ImageArtCropKey, PresignExpiry, ct)
            : null,
    };

    public async Task<CardPrintingDto> MapAsync(
        CardPrinting printing,
        string setName,
        CardPriceDto? price,
        CancellationToken ct)
    {
        var imageUrls = await MapImagesAsync(printing, ct);
        var faces = await MapFacesAsync(printing.Faces, ct);

        return new CardPrintingDto
        {
            Id = printing.Id,
            OracleId = printing.OracleId,
            Name = printing.Name,
            SetCode = printing.SetCode,
            SetName = setName,
            CollectorNumber = printing.CollectorNumber,
            ColorIdentity = printing.ColorIdentity,
            Rarity = printing.Rarity,
            ManaCost = printing.ManaCost,
            Cmc = printing.Cmc,
            Images = imageUrls,
            Prices = price,
            Faces = faces,
        };
    }

    public async Task<IReadOnlyList<CardFaceDto>?> MapFacesAsync(
        IReadOnlyList<CardFace>? faces,
        CancellationToken ct)
    {
        if (faces is null || faces.Count == 0)
        {
            return null;
        }

        var result = new List<CardFaceDto>(faces.Count);
        foreach (var face in faces.OrderBy(f => f.FaceIndex))
        {
            CardImageUrls? faceImages = null;
            if (face.ImageObjectKey is { Length: > 0 } || face.ImageArtCropKey is { Length: > 0 })
            {
                faceImages = new CardImageUrls
                {
                    Normal = face.ImageObjectKey is { Length: > 0 }
                        ? await _images.CreatePresignedGetUrlAsync(face.ImageObjectKey, PresignExpiry, ct)
                        : null,
                    ArtCrop = face.ImageArtCropKey is { Length: > 0 }
                        ? await _images.CreatePresignedGetUrlAsync(face.ImageArtCropKey, PresignExpiry, ct)
                        : null,
                };
            }

            result.Add(new CardFaceDto
            {
                FaceIndex = face.FaceIndex,
                Name = face.Name,
                ManaCost = face.ManaCost,
                TypeLine = face.TypeLine,
                OracleText = face.OracleText,
                Power = face.Power,
                Toughness = face.Toughness,
                Images = faceImages,
            });
        }

        return result;
    }
}
