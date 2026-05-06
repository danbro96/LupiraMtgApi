using System.Text.Json;
using System.Text.Json.Serialization;

namespace LupiraMtgApi.Services.Ocr;

public sealed class FlorenceOcrService : IOcrService
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private readonly HttpClient _httpClient;
    private readonly ILogger<FlorenceOcrService> _logger;

    public FlorenceOcrService(HttpClient httpClient, ILogger<FlorenceOcrService> logger)
    {
        _httpClient = httpClient;
        _logger = logger;
    }

    public async Task<string> ReadTextAsync(byte[] imageBytes, string mediaType, CancellationToken ct)
    {
        var request = new FlorenceOcrRequest
        {
            Image = Convert.ToBase64String(imageBytes),
            MediaType = mediaType,
        };

        using var response = await _httpClient.PostAsJsonAsync("ocr", request, SerializerOptions, ct);
        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(ct);
            _logger.LogWarning(
                "FlorenceApi /ocr returned {Status}: {Body}",
                (int) response.StatusCode,
                body);
            response.EnsureSuccessStatusCode();
        }

        var result = await response.Content.ReadFromJsonAsync<FlorenceOcrResult>(SerializerOptions, ct)
            ?? throw new InvalidOperationException("FlorenceApi /ocr returned null body.");

        return result.Text ?? string.Empty;
    }

    public async Task<OcrRegions> ReadRegionsAsync(byte[] imageBytes, string mediaType, CancellationToken ct)
    {
        var request = new FlorenceOcrRequest
        {
            Image = Convert.ToBase64String(imageBytes),
            MediaType = mediaType,
        };

        using var response = await _httpClient.PostAsJsonAsync("ocr/regions", request, SerializerOptions, ct);
        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(ct);
            _logger.LogWarning(
                "FlorenceApi /ocr/regions returned {Status}: {Body}",
                (int) response.StatusCode,
                body);
            response.EnsureSuccessStatusCode();
        }

        var result = await response.Content.ReadFromJsonAsync<FlorenceOcrRegionsResult>(SerializerOptions, ct)
            ?? throw new InvalidOperationException("FlorenceApi /ocr/regions returned null body.");

        var raw = result.Regions ?? Array.Empty<FlorenceOcrRegion>();
        var regions = new List<OcrRegion>(raw.Length);
        foreach (var r in raw)
        {
            if (r.Quad is null || r.Quad.Length != 8 || r.Box is null)
            {
                continue;
            }

            regions.Add(new OcrRegion
            {
                Text = r.Text ?? string.Empty,
                Quad = r.Quad,
                Box = new BoundingBox
                {
                    XMin = r.Box.XMin,
                    YMin = r.Box.YMin,
                    XMax = r.Box.XMax,
                    YMax = r.Box.YMax,
                },
                Rotation = r.Rotation,
                Confidence = r.Confidence,
            });
        }

        return new OcrRegions { Regions = regions };
    }

    private sealed class FlorenceOcrRequest
    {
        public required string Image { get; set; }

        public required string MediaType { get; set; }
    }

    private sealed class FlorenceOcrResult
    {
        public string? Text { get; set; }
    }

    private sealed class FlorenceOcrRegionsResult
    {
        public FlorenceOcrRegion[]? Regions { get; set; }

        public FlorenceImageSize? Image { get; set; }
    }

    private sealed class FlorenceOcrRegion
    {
        public string? Text { get; set; }

        public double[]? Quad { get; set; }

        public FlorenceBox? Box { get; set; }

        public double Rotation { get; set; }

        public double Confidence { get; set; }
    }

    private sealed class FlorenceBox
    {
        public double XMin { get; set; }

        public double YMin { get; set; }

        public double XMax { get; set; }

        public double YMax { get; set; }
    }

    private sealed class FlorenceImageSize
    {
        public int Width { get; set; }

        public int Height { get; set; }
    }
}
