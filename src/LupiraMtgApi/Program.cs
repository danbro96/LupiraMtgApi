using LupiraMtgApi;
using LupiraMtgApi.Auth;
using LupiraMtgApi.Data;
using LupiraMtgApi.Endpoints;
using LupiraMtgApi.Endpoints.Admin;
using LupiraMtgApi.Endpoints.Cards;
using LupiraMtgApi.Endpoints.Collections;
using LupiraMtgApi.Endpoints.Me;
using LupiraMtgApi.Endpoints.Scans;
using LupiraMtgApi.Endpoints.Selections;
using LupiraMtgApi.Handlers;
using LupiraMtgApi.Jobs;
using LupiraMtgApi.Services;
using Marten;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using Microsoft.OpenApi;
using OpenTelemetry.Logs;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using Scalar.AspNetCore;
using System.Text.Json.Serialization;
using System.Threading.RateLimiting;

var builder = WebApplication.CreateBuilder(args);

var connectionString = builder.Configuration.GetConnectionString("Postgres")
    ?? throw new InvalidOperationException("ConnectionStrings:Postgres is required.");

builder.Services
    .AddMarten(opts =>
    {
        opts.Connection(connectionString);
        opts.DatabaseSchemaName = "public";
        opts.UseSystemTextJsonForSerialization();
        MartenRegistrations.Configure(opts);
    })
    .UseLightweightSessions();

// Build the EF data source explicitly so we can enable dynamic JSON — required
// to write Dictionary<string, decimal> (CardPrinting.Prices) into a jsonb column
// under Npgsql 8+, which otherwise refuses to serialize unmapped complex types.
var efDataSourceBuilder = new Npgsql.NpgsqlDataSourceBuilder(connectionString);
efDataSourceBuilder.EnableDynamicJson();
var efDataSource = efDataSourceBuilder.Build();

builder.Services.AddDbContext<LupiraMtgDbContext>(opts =>
{
    opts.UseNpgsql(efDataSource, npgsql =>
    {
        npgsql.MigrationsHistoryTable("__EFMigrationsHistory", LupiraMtgDbContext.Schema);
    });
});

builder.Services.Configure<MinioImageStoreOptions>(builder.Configuration.GetSection("Minio"));
builder.Services.Configure<ScryfallSyncOptions>(builder.Configuration.GetSection("ScryfallSync"));
builder.Services.Configure<FlorenceOcrOptions>(builder.Configuration.GetSection("Florence"));

builder.Services.AddSingleton<IImageStore, MinioImageStore>();
builder.Services.AddSingleton<PHashService>();
builder.Services.AddSingleton<PHashIndex>();

builder.Services.AddHttpClient<ICardCatalogSource, ScryfallCatalogSource>(client =>
{
    client.BaseAddress = new Uri("https://api.scryfall.com/");
    client.DefaultRequestHeaders.UserAgent.ParseAdd("LupiraMtgApi/0.1 (+https://github.com/danbro96/LupiraMtgApi)");
    client.DefaultRequestHeaders.Accept.ParseAdd("application/json");
    client.Timeout = TimeSpan.FromMinutes(5);
});

builder.Services.AddHttpClient<IOcrService, FlorenceOcrService>((sp, client) =>
{
    var opts = sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<FlorenceOcrOptions>>().Value;
    var url = opts.Url.EndsWith('/') ? opts.Url : opts.Url + "/";
    client.BaseAddress = new Uri(url);
    if (!string.IsNullOrEmpty(opts.ApiKey))
    {
        client.DefaultRequestHeaders.Add("X-API-Key", opts.ApiKey);
    }

    client.DefaultRequestHeaders.Accept.ParseAdd("application/json");
    client.Timeout = TimeSpan.FromSeconds(Math.Max(5, opts.TimeoutSeconds));
});

builder.Services.AddSingleton<ScryfallSyncRunner>();
builder.Services.AddHostedService<ScryfallSyncJob>();
builder.Services.AddHostedService<PHashIndexBootstrapper>();

builder.Services.AddScoped<CardPrintingMapper>();
builder.Services.AddScoped<CardInstanceHydrator>();
builder.Services.AddScoped<CardSearchHandler>();
builder.Services.AddScoped<AdminSyncHandler>();
builder.Services.AddScoped<MeHandler>();
builder.Services.AddScoped<ScanHandler>();
builder.Services.AddScoped<CollectionsHandler>();
builder.Services.AddScoped<SelectionsHandler>();
builder.Services.AddScoped<MyCardsHandler>();

builder.Services.AddOpenApi("v1", options =>
{
    options.AddDocumentTransformer((document, context, _) =>
    {
        document.Info = new()
        {
            Title = "Lupira MTG API",
            Version = "v1",
            Description =
                "Backend for the Lupira MTG mobile app — Magic: The Gathering card metadata, " +
                "scan-based recognition, and per-user collection management. " +
                "Authenticate with a Bearer token issued by Authentik (OIDC).",
        };
        document.Components ??= new();
        document.Components.SecuritySchemes ??= new Dictionary<string, IOpenApiSecurityScheme>();
        document.Components.SecuritySchemes["Bearer"] = new OpenApiSecurityScheme
        {
            Type = SecuritySchemeType.Http,
            Scheme = "bearer",
            Description =
                "PoC device token from `POST /me/register`. Send as `Authorization: Bearer lmtg_<token>`. " +
                "Replaced by Authentik OIDC tokens in the future Path C migration (see plan).",
        };
        return Task.CompletedTask;
    });
    options.AddOperationTransformer((operation, context, _) =>
    {
        var endpointMetadata = context.Description.ActionDescriptor.EndpointMetadata;
        var requiresAuth = endpointMetadata.OfType<IAuthorizeData>().Any()
                        && !endpointMetadata.OfType<IAllowAnonymous>().Any();
        if (requiresAuth)
        {
            operation.Security ??= new List<OpenApiSecurityRequirement>();
            operation.Security.Add(new OpenApiSecurityRequirement
            {
                [new OpenApiSecuritySchemeReference("Bearer", context.Document)] = new List<string>(),
            });
        }

        return Task.CompletedTask;
    });
});

builder.Services
    .AddAuthentication(DeviceTokenAuthOptions.SchemeName)
    .AddScheme<DeviceTokenAuthOptions, DeviceTokenAuthenticationHandler>(
        DeviceTokenAuthOptions.SchemeName,
        opts =>
        {
            builder.Configuration.GetSection("Auth").Bind(opts);
        });
builder.Services.AddAuthorization();

builder.Services.AddRateLimiter(o =>
{
    o.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    var permitsPerMinute = builder.Configuration.GetValue("RateLimit:RequestsPerMinute", 120);
    o.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(ctx =>
    {
        var key = ctx.User.FindFirst("sub")?.Value
               ?? ctx.Connection.RemoteIpAddress?.ToString()
               ?? "anon";
        return RateLimitPartition.GetTokenBucketLimiter(key, _ => new TokenBucketRateLimiterOptions
        {
            TokenLimit = permitsPerMinute,
            TokensPerPeriod = permitsPerMinute,
            ReplenishmentPeriod = TimeSpan.FromMinutes(1),
            QueueLimit = 0,
            AutoReplenishment = true,
        });
    });
});

var allowedOrigins = builder.Configuration.GetSection("Auth:AllowedOrigins").Get<string[]>() ?? Array.Empty<string>();
if (allowedOrigins.Length > 0)
{
    builder.Services.AddCors(o => o.AddDefaultPolicy(p =>
        p.WithOrigins(allowedOrigins).AllowAnyHeader().AllowAnyMethod()));
}

builder.Services.Configure<Microsoft.AspNetCore.Http.Json.JsonOptions>(o =>
{
    o.SerializerOptions.PropertyNameCaseInsensitive = true;
    o.SerializerOptions.Converters.Add(new JsonStringEnumConverter());
});

builder.WebHost.ConfigureKestrel(o => o.Limits.MaxRequestBodySize = 4_000_000);

var otlpEndpoint = builder.Configuration["OTEL_EXPORTER_OTLP_ENDPOINT"];
if (!string.IsNullOrWhiteSpace(otlpEndpoint))
{
    builder.Services.AddOpenTelemetry()
        .ConfigureResource(r => r.AddService(
            serviceName: "lupira-mtg-api",
            serviceVersion: typeof(Program).Assembly.GetName().Version?.ToString() ?? "0.0.0"))
        .WithTracing(t => t
            .AddSource("LupiraMtgApi.*")
            .AddAspNetCoreInstrumentation(o => o.RecordException = true)
            .AddHttpClientInstrumentation()
            .AddOtlpExporter())
        .WithMetrics(m => m
            .AddAspNetCoreInstrumentation()
            .AddHttpClientInstrumentation()
            .AddRuntimeInstrumentation()
            .AddOtlpExporter());

    builder.Logging.AddOpenTelemetry(o =>
    {
        o.IncludeFormattedMessage = true;
        o.IncludeScopes = true;
        o.AddOtlpExporter();
    });
}

var app = builder.Build();

await using (var scope = app.Services.CreateAsyncScope())
{
    var db = scope.ServiceProvider.GetRequiredService<LupiraMtgDbContext>();
    await db.Database.MigrateAsync();
}

if (allowedOrigins.Length > 0) app.UseCors();
app.UseAuthentication();
app.UseAuthorization();
app.UseRateLimiter();

app.MapOpenApi("/openapi/{documentName}.json").AllowAnonymous();
app.MapScalarApiReference("/scalar", o => o
        .WithTitle("Lupira MTG API")
        .WithTheme(ScalarTheme.BluePlanet))
    .AllowAnonymous();

app.MapGet("/", () => TypedResults.Redirect("/scalar"))
   .ExcludeFromDescription()
   .AllowAnonymous();

app.MapHealthEndpoint();

app.MapRegisterDevice();
app.MapWhoAmI().RequireAuthorization();

app.MapGetPrinting().RequireAuthorization();
app.MapCardSearch().RequireAuthorization();
app.MapScan().RequireAuthorization();
app.MapAdminSync().RequireAuthorization();
app.MapMyCards().RequireAuthorization();

app.MapCollections();
app.MapSelections();

app.Run();
