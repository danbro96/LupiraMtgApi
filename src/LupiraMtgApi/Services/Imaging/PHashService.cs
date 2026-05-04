using CoenM.ImageHash;
using CoenM.ImageHash.HashAlgorithms;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace LupiraMtgApi.Services.Imaging;

public sealed class PHashService
{
    private readonly IImageHash _algorithm = new PerceptualHash();

    public async Task<long> ComputeAsync(Stream image, CancellationToken ct)
    {
        using var img = await Image.LoadAsync<Rgba32>(image, ct);
        var hash = _algorithm.Hash(img);
        return unchecked((long) hash);
    }

    public static int HammingDistance(long a, long b)
    {
        var xor = unchecked((ulong) a) ^ unchecked((ulong) b);
        return System.Numerics.BitOperations.PopCount(xor);
    }

    public long Compute(Stream image)
    {
        using var img = Image.Load<Rgba32>(image);
        var hash = _algorithm.Hash(img);
        return unchecked((long) hash);
    }
}
