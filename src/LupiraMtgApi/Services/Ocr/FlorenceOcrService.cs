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

        var labels = result.Labels ?? Array.Empty<string>();
        var quads = result.QuadBoxes ?? Array.Empty<double[]>();
        var count = Math.Min(labels.Length, quads.Length);

        var regions = new List<OcrRegion>(count);
        for (var i = 0; i < count; i++)
        {
            var quad = quads[i];
            if (quad is null || quad.Length != 8)
            {
                continue;
            }

            regions.Add(new OcrRegion
            {
                Text = labels[i] ?? string.Empty,
                QuadBox = quad,
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
        public double[][]? QuadBoxes { get; set; }

        public string[]? Labels { get; set; }
    }
}
