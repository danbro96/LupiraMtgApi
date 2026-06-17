using JasperFx;
using LupiraMtgApi.Auth;
using LupiraMtgApi.Catalog.Data;
using LupiraMtgApi.Collections.Data;
using LupiraMtgApi.Endpoints;
using LupiraMtgApi.Endpoints.Admin;
using LupiraMtgApi.Endpoints.Cards;
using LupiraMtgApi.Endpoints.Collections;
using LupiraMtgApi.Endpoints.Me;
using LupiraMtgApi.Endpoints.Scans;
using LupiraMtgApi.Endpoints.Selections;
using LupiraMtgApi.Endpoints.Sets;
using LupiraMtgApi.Handlers;
using LupiraMtgApi.Recognition.Data;
using LupiraMtgApi.Sync;
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
// `dotnet-getdocument` (Microsoft.Extensions.ApiDescription.Server's build-time
// OpenAPI emitter) loads this assembly and runs Main() to obtain the document
// provider — but it never calls `app.Run()`. When we detect that mode, we
// skip startup work that needs external services (DB migrate, etc.) so the
// build can complete on a developer machine without Postgres available.
var isOpenApiBuild = Environment.GetCommandLineArgs()
    .Any(a => a.Contains("getdocument", StringComparison.OrdinalIgnoreCase));

// One-shot schema apply: `dotnet run -- --apply-schema` applies EF migrations + Marten schema
// and exits. Production composes with AutoCreate.None and runs this deliberately, never
// auto-migrating on a normal boot.
var applySchema = args.Contains("--apply-schema");

var builder = WebApplication.CreateBuilder(args);

var connectionString = builder.Configuration.GetConnectionString("Postgres")
    ?? throw new InvalidOperationException("ConnectionStrings:Postgres is required.");

builder.Services
    .AddMarten(opts =>
    {
        opts.Connection(connectionString);
        opts.DatabaseSchemaName = "users";
        opts.UseSystemTextJsonForSerialization();

        // Prod controls schema explicitly (see --apply-schema); only dev auto-creates.
        opts.AutoCreateSchemaObjects = builder.Environment.IsDevelopment()
            ? AutoCreate.CreateOrUpdate
            : AutoCreate.None;

        // Each context contributes its own document registrations.
        CollectionsMartenRegistrations.Configure(opts);
        RecognitionMartenRegistrations.Configure(opts);
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

// Liveness (/livez) + readiness (/readyz, pings Postgres) probes.
builder.Services.AddAppHealthChecks();

// The three bounded contexts — each registers its own Application + Infrastructure services.
builder.Services.AddCatalog(builder.Configuration);
builder.Services.AddRecognition(builder.Configuration);
builder.Services.AddCollections();

// Cross-context Scryfall sync orchestration lives in the host (it writes Catalog data + images and
// rebuilds Recognition's indexes, so it sits above both contexts).
builder.Services.Configure<ScryfallSyncOptions>(builder.Configuration.GetSection("ScryfallSync"));
builder.Services.AddSingleton<ScryfallSyncRunner>();
builder.Services.AddHostedService<ScryfallSyncJob>();

// Host transport adapters (thin) over the context Application services.
builder.Services.AddScoped<CardCatalogHandler>();
builder.Services.AddScoped<SetsHandler>();
builder.Services.AddScoped<SetTypeWeightHandler>();
builder.Services.AddScoped<ScanHandler>();
builder.Services.AddScoped<ScanFeedbackHandler>();
builder.Services.AddScoped<ScanHistoryHandler>();
builder.Services.AddScoped<CollectionsHandler>();
builder.Services.AddScoped<SelectionsHandler>();
builder.Services.AddScoped<MyCardsHandler>();
builder.Services.AddScoped<MeHandler>();
builder.Services.AddScoped<AdminSyncHandler>();

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
            .AddMeter("LupiraMtgApi.*")
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

// One-shot schema apply (prod): EF migrations + Marten schema, then exit.
if (applySchema)
{
    await using var scope = app.Services.CreateAsyncScope();
    var db = scope.ServiceProvider.GetRequiredService<LupiraMtgDbContext>();
    await db.Database.MigrateAsync();
    var store = scope.ServiceProvider.GetRequiredService<IDocumentStore>();
    await store.Storage.ApplyAllConfiguredChangesToDatabaseAsync();
    return;
}

// Dev convenience: auto-apply EF migrations on boot. Prod uses --apply-schema instead.
if (!isOpenApiBuild && app.Environment.IsDevelopment())
{
    await using var scope = app.Services.CreateAsyncScope();
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

app.MapAppHealthChecks(app.Environment);

app.MapRegisterDevice();
app.MapWhoAmI().RequireAuthorization();
app.MapUpdateMe().RequireAuthorization();

app.MapScan().RequireAuthorization();
app.MapScanFeedback().RequireAuthorization();
app.MapAdminSync().RequireAuthorization();
app.MapSetTypeWeights();
app.MapMyCards().RequireAuthorization();
app.MapScanHistory();

app.MapCards();
app.MapSets();
app.MapCollections();
app.MapSelections();

app.Run();
