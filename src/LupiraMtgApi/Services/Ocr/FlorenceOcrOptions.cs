namespace LupiraMtgApi.Services.Ocr;

public sealed class FlorenceOcrOptions
{
    public string Url { get; set; } = "http://florence-api:8080";

    public string ApiKey { get; set; } = string.Empty;

    public int TimeoutSeconds { get; set; } = 30;
}
