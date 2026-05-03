using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace LupiraMtgApi.Services;

public sealed class FlorenceOcrService : IOcrService
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private readonly HttpClient httpClient;
    private readonly ILogger<FlorenceOcrService> logger;

    public FlorenceOcrService(HttpClient httpClient, ILogger<FlorenceOcrService> logger)
    {
        this.httpClient = httpClient;
        this.logger = logger;
    }

    public async Task<string> ReadTextAsync(byte[] imageBytes, string mediaType, CancellationToken ct)
    {
        var request = new FlorenceOcrRequest
        {
            Image = Convert.ToBase64String(imageBytes),
            MediaType = mediaType,
        };

        using var response = await this.httpClient.PostAsJsonAsync("ocr", request, SerializerOptions, ct);
        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(ct);
            this.logger.LogWarning(
                "FlorenceApi /ocr returned {Status}: {Body}",
                (int)response.StatusCode,
                body);
            response.EnsureSuccessStatusCode();
        }

        var result = await response.Content.ReadFromJsonAsync<FlorenceOcrResult>(SerializerOptions, ct)
            ?? throw new InvalidOperationException("FlorenceApi /ocr returned null body.");

        return result.Text ?? string.Empty;
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
}
