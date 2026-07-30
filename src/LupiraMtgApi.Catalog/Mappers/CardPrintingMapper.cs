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

    public async Task<CardImageUrls> MapImagesAsync(CardPrinting printing, CancellationToken ct)
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

        return imageUrls;
    }

    public async Task<CardPrintingResponse> MapAsync(
        CardPrinting printing,
        string setName,
        CardPriceResponse? price,
        CancellationToken ct)
    {
        var imageUrls = await MapImagesAsync(printing, ct);
        var faces = await MapFacesAsync(printing.Faces, ct);

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
            ManaCost = printing.ManaCost,
            Cmc = printing.Cmc,
            Images = imageUrls,
            Prices = price,
            Faces = faces,
        };
    }

    public async Task<IReadOnlyList<CardFaceResponse>?> MapFacesAsync(
        IReadOnlyList<CardFace>? faces,
        CancellationToken ct)
    {
        if (faces is null || faces.Count == 0)
        {
            return null;
        }

        var result = new List<CardFaceResponse>(faces.Count);
        foreach (var face in faces.OrderBy(f => f.FaceIndex))
        {
            CardImageUrls? faceImages = null;
            if (face.ImageObjectKey is { Length: > 0 } || face.ImageArtCropKey is { Length: > 0 })
            {
                faceImages = new CardImageUrls();
                if (face.ImageObjectKey is { Length: > 0 })
                {
                    faceImages.Normal = await _images.CreatePresignedGetUrlAsync(face.ImageObjectKey, PresignExpiry, ct);
                }

                if (face.ImageArtCropKey is { Length: > 0 })
                {
                    faceImages.ArtCrop = await _images.CreatePresignedGetUrlAsync(face.ImageArtCropKey, PresignExpiry, ct);
                }
            }

            result.Add(new CardFaceResponse
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
