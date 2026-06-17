using LupiraMtgApi.Recognition.Application;
using LupiraMtgApi.Recognition.Application.Pipeline;
using LupiraMtgApi.Recognition.Application.Steps;
using LupiraMtgApi.Recognition.Infrastructure.Imaging;
using LupiraMtgApi.Recognition.Infrastructure.Jobs;
using LupiraMtgApi.Recognition.Infrastructure.Ocr;
using LupiraMtgApi.Recognition.Infrastructure.SetSymbol;

namespace Microsoft.Extensions.DependencyInjection;

/// <summary>
/// Registers the Recognition bounded context — scoring config, the imaging/OCR/set-symbol
/// infrastructure, the ordered scan pipeline, the scan Application services, and the startup index
/// bootstrappers. Depends only on the DI abstractions, not ASP.NET.
/// </summary>
public static class RecognitionServiceCollectionExtensions
{
    public static IServiceCollection AddRecognition(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<FlorenceOcrOptions>(configuration.GetSection("Florence"));
        services.Configure<ScanScoringOptions>(configuration.GetSection("Scan:Scoring"));

        services.AddSingleton<PHashService>();
        services.AddSingleton<PHashIndex>();
        services.AddSingleton<FullCardPHashIndex>();
        services.AddSingleton<CardCropService>();
        services.AddSingleton<CardZoneClassifier>();
        services.AddSingleton<SetSymbolRasterizer>();
        services.AddSingleton<SetSymbolIndex>();
        services.AddSingleton<SetSymbolDetector>();
        services.AddSingleton<ScanPHashRunner>();

        services.AddHttpClient<IOcrService, FlorenceOcrService>((sp, client) =>
        {
            var opts = sp.GetRequiredService<IOptions<FlorenceOcrOptions>>().Value;
            var url = opts.Url.EndsWith('/') ? opts.Url : opts.Url + "/";
            client.BaseAddress = new Uri(url);
            if (!string.IsNullOrEmpty(opts.ApiKey))
            {
                client.DefaultRequestHeaders.Add("X-API-Key", opts.ApiKey);
            }

            client.DefaultRequestHeaders.Accept.ParseAdd("application/json");
            client.Timeout = TimeSpan.FromSeconds(Math.Max(5, opts.TimeoutSeconds));
        });

        services.AddScoped<CardZoneScorer>();

        // Scan pipeline: each step registered as IScanStep in execution order. DI resolves
        // IEnumerable<IScanStep> in registration order, so the order below IS the pipeline.
        services.AddScoped<IScanStep, UploadOriginalStep>();
        services.AddScoped<IScanStep, CropStep>();
        services.AddScoped<IScanStep, PrimaryRecognitionStep>();
        services.AddScoped<IScanStep, ZoneClassifyStep>();
        services.AddScoped<IScanStep, ZoneScoreStep>();
        services.AddScoped<IScanStep, RotationRetryStep>();
        services.AddScoped<IScanStep, FusionStep>();
        services.AddScoped<IScanStep, SetTypeWeightStep>();
        services.AddScoped<IScanStep, HydrateStep>();
        services.AddScoped<IScanStep, ConfidenceStep>();
        services.AddScoped<IScanStep, RecordOutcomeStep>();
        services.AddScoped<IScanStep, PersistScanLogStep>();
        services.AddScoped<ScanPipeline>();

        services.AddScoped<ScanService>();
        services.AddScoped<ScanFeedbackService>();
        services.AddScoped<ScanHistoryService>();

        services.AddHostedService<PHashIndexBootstrapper>();
        services.AddHostedService<FullCardPHashIndexBootstrapper>();
        services.AddHostedService<SetSymbolIndexBootstrapper>();

        return services;
    }
}
