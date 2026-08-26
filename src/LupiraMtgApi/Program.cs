using LupiraMtgApi.Http;
using System.Globalization;
using System.Diagnostics;
using System.Text.Json.Serialization;
using System.Threading.RateLimiting;
using JasperFx;
using LupiraMtgApi.Catalog.Data;
using LupiraMtgApi.Catalog.Infrastructure.Storage;
using LupiraMtgApi.Collections.Data;
using LupiraMtgApi.Dependencies;
using LupiraMtgApi.Recognition.Infrastructure.Ocr;
using LupiraMtgApi.Endpoints;
using LupiraMtgApi.Endpoints.Admin;
using LupiraMtgApi.Endpoints.Cards;
using LupiraMtgApi.Endpoints.Collections;
using LupiraMtgApi.Endpoints.Me;
using LupiraMtgApi.Endpoints.Scans;
using LupiraMtgApi.Endpoints.Selections;
using LupiraMtgApi.Endpoints.Sets;
using LupiraMtgApi.Handlers;
using LupiraMtgApi.Pricing.Data;
using LupiraMtgApi.Recognition.Data;
using LupiraMtgApi.Workers;
using Marten;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using Microsoft.OpenApi;
using OpenTelemetry.Logs;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using Scalar.AspNetCore;
using Weasel.Core;
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
        // Store enums as strings (not integers) so reordering/inserting an enum value can't reinterpret
        // stored documents; matches the platform convention across the Lupira APIs.
        opts.UseSystemTextJsonForSerialization(EnumStorage.AsString);

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
// to (de)serialize List<CardFace> (CardPrinting.Faces) against a jsonb column
// under Npgsql 8+, which otherwise refuses to handle unmapped complex types.
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

// Pricing context — typed decimal columns (no jsonb), so a plain connection is enough; its own
// migrations chain lives in the `prices` schema, separate from Catalog's `cards.__EFMigrationsHistory`.
builder.Services.AddDbContext<PricingDbContext>(opts =>
{
    opts.UseNpgsql(connectionString, npgsql =>
    {
        npgsql.MigrationsHistoryTable("__EFMigrationsHistory", PricingDbContext.Schema);
    });
});

// Liveness (/livez) + readiness (/readyz, pings Postgres) probes.
builder.Services.AddAppHealthChecks();

// The three bounded contexts — each registers its own Application + Infrastructure services.
builder.Services.AddCatalog(builder.Configuration);
builder.Services.AddRecognition(builder.Configuration);
builder.Services.AddCollections();
builder.Services.AddPricing(builder.Configuration);

// Non-gating dependency probe (/depz): edges derive from the same config the contexts bind.
builder.Services.Configure<DepzOptions>(builder.Configuration.GetSection(DepzOptions.SectionName));
var depzOpts = builder.Configuration.GetSection(DepzOptions.SectionName).Get<DepzOptions>() ?? new DepzOptions();
builder.Services.AddSingleton(DependencyTargets.From(
    builder.Configuration.GetSection("Florence").Get<FlorenceOcrOptions>() ?? new FlorenceOcrOptions(),
    builder.Configuration.GetSection("S3").Get<S3ImageStoreOptions>() ?? new S3ImageStoreOptions()));
builder.Services.AddSingleton<DependencyReportCache>();
builder.Services.AddSingleton<DependencyProbe>();
builder.Services.AddHttpClient(DependencyProbe.ProbeClientName, c => c.Timeout = depzOpts.ProbeTimeout);
if (depzOpts.Enabled)
    builder.Services.AddHostedService<DependencyPollWorker>();

// Cross-context Scryfall sync orchestration lives in the host (it writes Catalog data + images and
// rebuilds Recognition's indexes, so it sits above both contexts).
builder.Services.Configure<ScryfallSyncOptions>(builder.Configuration.GetSection("ScryfallSync"));
builder.Services.AddSingleton<ScryfallSyncRunner>();
builder.Services.AddHostedService<ScryfallSyncJob>();

// Host transport adapters (thin) over the context Application services.
builder.Services.AddScoped<CardCatalogHandler>();
builder.Services.AddScoped<CardPriceHistoryHandler>();
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

builder.Services.AddProblemDetails(o => o.CustomizeProblemDetails = ctx =>
    ctx.ProblemDetails.Extensions["traceId"] = Activity.Current?.Id ?? ctx.HttpContext.TraceIdentifier);
builder.Services.AddExceptionHandler<ProblemExceptionHandler>();

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
            BearerFormat = "JWT",
            Description =
                "Authentik OIDC access token (authorization-code + PKCE, public client `lupira-mtg`). " +
                "Send as `Authorization: Bearer <jwt>`.",
        };
        document.Components.Schemas ??= new Dictionary<string, IOpenApiSchema>();
        document.Components.Schemas["ProblemDetails"] = ProblemDetailsSchema();
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
            AddProblem(operation, context.Document, StatusCodes.Status401Unauthorized, "Unauthorized");
        }

        // The cross-cutting code no endpoint declares — ProblemExceptionHandler produces it.

        AddProblem(operation, context.Document, StatusCodes.Status500InternalServerError, "Internal server error");


        // Bodyless 4xx/5xx come from the non-generic arms of the typed-result unions (NotFound,

        // UnauthorizedHttpResult). UseStatusCodePages fills them at runtime, so declare the shape.

        foreach (var code in operation.Responses?.Keys.ToList() ?? [])

        {

            if (code.Length != 3 || code[0] is not ('4' or '5')) continue;

            var existing = operation.Responses![code];

            if (existing.Content is { Count: > 0 }) continue;

            operation.Responses[code] = new OpenApiResponse

            { Description = existing.Description, Content = ProblemContent(context.Document) };

        }


        return Task.CompletedTask;
    });
});

// Every error response carries the same shape, so a generated client types its error once instead of
// falling back to `void`.
static Dictionary<string, OpenApiMediaType> ProblemContent(OpenApiDocument document) =>
    new() { ["application/problem+json"] = new() { Schema = new OpenApiSchemaReference("ProblemDetails", document) } };

static void AddProblem(OpenApiOperation operation, OpenApiDocument document, int status, string description)
{
    var code = status.ToString(CultureInfo.InvariantCulture);
    operation.Responses ??= [];
    if (operation.Responses.ContainsKey(code)) return;
    operation.Responses[code] = new OpenApiResponse { Description = description, Content = ProblemContent(document) };
}

/// RFC 9457. Declared here because nothing returns the CLR type directly, so the generator never emits it.
static OpenApiSchema ProblemDetailsSchema() => new()
{
    Type = JsonSchemaType.Object,
    Description = "RFC 9457 problem details.",
    Properties = new Dictionary<string, IOpenApiSchema>
    {
        ["type"] = new OpenApiSchema { Type = JsonSchemaType.String | JsonSchemaType.Null },
        ["title"] = new OpenApiSchema { Type = JsonSchemaType.String | JsonSchemaType.Null },
        ["status"] = new OpenApiSchema { Type = JsonSchemaType.Integer | JsonSchemaType.Null, Format = "int32" },
        ["detail"] = new OpenApiSchema { Type = JsonSchemaType.String | JsonSchemaType.Null },
        ["instance"] = new OpenApiSchema { Type = JsonSchemaType.String | JsonSchemaType.Null },
        ["traceId"] = new OpenApiSchema { Type = JsonSchemaType.String | JsonSchemaType.Null },
    },
};

builder.Services
    .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        // Authority (Authentik issuer) + Audience (the `lupira-mtg` client id) come from config;
        // OIDC discovery off the Authority fetches the JWKS, so tokens are validated by
        // signature/issuer/audience/lifetime out of the box.
        builder.Configuration.GetSection("Auth").Bind(options);
        // Keep the raw `sub`/`groups` claim names instead of remapping them to the legacy SOAP URIs.
        options.MapInboundClaims = false;
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
    o.SerializerOptions.NumberHandling = JsonNumberHandling.Strict;
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
            .AddAspNetCoreInstrumentation(o =>
            {
                o.RecordException = true;
                // Health probes are polled constantly by docker + devops-monitor; their spans add nothing.
                o.Filter = ctx => ctx.Request.Path != "/livez" && ctx.Request.Path != "/readyz"
                    && ctx.Request.Path != "/depz";
            })
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
    var pricingDb = scope.ServiceProvider.GetRequiredService<PricingDbContext>();
    await pricingDb.Database.MigrateAsync();
    var store = scope.ServiceProvider.GetRequiredService<IDocumentStore>();
    await store.Storage.ApplyAllConfiguredChangesToDatabaseAsync();
    Console.WriteLine("Schema applied.");
    return;
}

// Dev convenience: auto-apply EF migrations on boot. Prod uses --apply-schema instead.
if (!isOpenApiBuild && app.Environment.IsDevelopment())
{
    await using var scope = app.Services.CreateAsyncScope();
    var db = scope.ServiceProvider.GetRequiredService<LupiraMtgDbContext>();
    await db.Database.MigrateAsync();
    var pricingDb = scope.ServiceProvider.GetRequiredService<PricingDbContext>();
    await pricingDb.Database.MigrateAsync();
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
app.MapDepz();

app.MapWhoAmI().RequireAuthorization();

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

// Exposed for WebApplicationFactory<Program> in the integration tests.
public partial class Program;
