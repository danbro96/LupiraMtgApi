# LupiraMtgApi

A self-hostable backend for a Magic: The Gathering collection app. It serves card metadata sourced
from [Scryfall](https://scryfall.com), recognizes physical cards from a photo (perceptual-hash + OCR
fusion), and tracks per-user collections and scanning selections.

- **Card catalog** — search functionally-distinct cards and drill into every printing; browse sets.
  Card art is served as **presigned object-store URLs**; the API never proxies image bytes.
- **Recognition** — `POST /scans` takes a photo and returns a confidence-ranked list of candidate
  printings, fusing a perceptual-hash match against the local art index with OCR of the card's text
  zones. Scan history and a feedback endpoint feed future ranker work.
- **Collections & selections** — per-user collections (add/move/remove, bulk ops, soft-delete) and
  short-lived "selections" that batch up recognized cards before committing them to a collection.
- **Catalog freshness** — a nightly background job incrementally syncs the Scryfall bulk data,
  downloads card art and set icons into object storage, and rebuilds the recognition indexes.

The surface is **REST only** (no MCP server). The OpenAPI document is served at
[`/openapi/v1.json`](http://localhost:8080/openapi/v1.json) and an interactive
[Scalar](https://scalar.com) UI at [`/scalar`](http://localhost:8080/scalar) (root `/` redirects
there).

## Tech stack

| Area | Choice |
|---|---|
| Runtime | .NET 10 minimal API (`net10.0`), thin endpoints → handlers |
| Reference data | EF Core 10 + Npgsql 10 (Scryfall catalog + device records) |
| User state | [Marten](https://martendb.com) 9.8 document store on Postgres |
| Image / hashing | SkiaSharp 3.119, SixLabors.ImageSharp 3.1, CoenM.ImageHash, Svg.Skia 5.1 |
| Object storage | Minio 7 client (any S3-compatible store) |
| OCR | external vision service over HTTP (e.g. a Florence-2 OCR endpoint) |
| API docs | Microsoft.AspNetCore.OpenApi 10 + Scalar.AspNetCore 2.16 |
| Scheduling | Cronos 0.13 (cron-driven sync) |
| Telemetry | OpenTelemetry 1.16 (OTLP exporter, opt-in) |
| Tests | xUnit |

Both Marten and EF Core share **one Postgres database**, separated by schema (see
[Persistence](#database--schema)). The code is split into three bounded-context libraries
(`Catalog`, `Recognition`, `Collections`) over a thin host — see [Project layout](#project-layout)
and [docs/architecture.md](docs/architecture.md).

## API surface

Every functional route requires a bearer token (see [Authentication](#authentication)). Only
`POST /me/register`, the health probes, and the docs endpoints are anonymous.

| Method | Route | Purpose |
|---|---|---|
| `POST` | `/me/register` | **Anonymous.** Mint a device token (`lmtg_…`), shown once |
| `GET` · `PATCH` | `/me` | Get / update the caller's device profile |
| `GET` | `/me/cards` | List every card the caller owns, across collections |
| `GET` | `/me/scans` · `/me/scans/{id}` | Scan history; full detail of one scan |
| `GET` | `/cards` | List/search functionally-distinct cards (filters, sort, paging) |
| `GET` | `/cards/{oracleId}` · `/{oracleId}/printings` · `/{oracleId}/printings/{printingId}` | Oracle card, its printings, one printing |
| `GET` | `/sets` · `/sets/{code}` | Browse sets; one set |
| `POST` | `/scans` | Recognize a card from a `multipart/form-data` `image` (≤4 MB) |
| `POST` | `/scans/{id}/feedback` | Report the actually-correct printing for a scan |
| `POST` `GET` `PATCH` `DELETE` | `/collections…` | CRUD + cards (`/cards`, `/move`, `/bulk`, `/bulk-delete`, `/bulk-move`) |
| `POST` `GET` `DELETE` | `/selections…` | Start a selection, add/remove cards, `/{id}/commit` into a collection |
| `POST` | `/admin/sync/run` | Trigger a Scryfall sync run synchronously |
| `GET` · `PUT` | `/admin/set-type-weights` · `/{setType}` | Read / upsert the set-type weights the ranker uses |
| `GET` | `/livez` · `/readyz` | **Anonymous.** Liveness; readiness (pings Postgres) |

Errors are RFC 7807 `application/problem+json` (`{ type, title, detail, status }`).

## Authentication

A lightweight **device-token** scheme — there is no external identity provider wired in.

1. `POST /me/register` mints a device identity (a `sub` GUID) and a 256-bit token of the form
   `lmtg_<base64url>`. The token is returned **once**; the server stores only its SHA-256 hash.
2. Send it on every other request:

   ```
   Authorization: Bearer lmtg_xxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxx
   ```

That register call is also the **local-dev on-ramp**: hit it once, copy the token, and authorize
the Scalar UI with it. Registration is IP rate-limited to curb spam.

## Run locally

**Prerequisites**

- [.NET 10 SDK](https://dotnet.microsoft.com/download)
- PostgreSQL reachable from your machine, with the `pg_trgm` extension available
  (`CREATE EXTENSION IF NOT EXISTS pg_trgm;` — used for fuzzy name/type/text search).
- *Optional, only for the full scan path:* an S3-compatible object store (for card art) and an OCR
  vision service. The API runs and serves the catalog without them; scans degrade gracefully.

**Configure** the connection string — edit
[`src/LupiraMtgApi/appsettings.Development.json`](src/LupiraMtgApi/appsettings.Development.json) or
set it via environment:

```bash
export ConnectionStrings__Postgres="Host=localhost;Port=5432;Database=lupira_mtg;Username=postgres;Password=postgres"
```

**Build, test, run**

```bash
dotnet restore LupiraMtgApi.slnx
dotnet build   LupiraMtgApi.slnx -c Release
dotnet test    LupiraMtgApi.slnx
dotnet run --project src/LupiraMtgApi        # → http://localhost:8080
```

In **Development**, the app auto-applies EF migrations and lets Marten create its schema on boot, so
an empty database is fine. Then:

- Open `http://localhost:8080/scalar` for the interactive docs.
- `POST /me/register`, copy the `lmtg_…` token, and authorize.
- The card catalog is empty until you run a sync (`POST /admin/sync/run`, auth required) — the first
  full sync downloads and hashes the entire Scryfall catalog and can take a long time.

## Configuration

All settings bind from `appsettings.json` and can be overridden by environment variables using the
ASP.NET `__` (double-underscore) convention for nested keys (e.g. `Minio__Bucket`).

| Variable | Default | Purpose |
|---|---|---|
| `ConnectionStrings__Postgres` | — *(required)* | Postgres connection string; shared by EF Core and Marten |
| `Auth__LastSeenWriteInterval` | `00:15:00` | How often a token's `LastSeenAt` is bumped (write-throttling) |
| `Auth__AllowedOrigins` | `[]` | CORS origins; empty disables CORS |
| `Florence__Url` | — | Base URL of the OCR/vision service |
| `Florence__ApiKey` | — | API key for the OCR/vision service |
| `Minio__Endpoint` | — | In-network object-store endpoint for uploads (e.g. `minio:9000`) |
| `Minio__PublicEndpoint` | — | Public base URL used to build presigned card-art URLs |
| `Minio__AccessKey` / `Minio__SecretKey` | — | Object-store credentials |
| `Minio__Bucket` | `lupira-mtg-cards` | Bucket for card images and set icons |
| `Minio__UseSsl` | `false` | TLS for the in-network upload path |
| `Scan__Scoring__*` | see `appsettings.json` | Recognition scoring weights, cutoffs and confidence thresholds (production-sane defaults) |
| `ScryfallSync__CronSchedule` | `0 4 * * *` | Cron schedule for the nightly catalog sync |
| `RateLimit__RequestsPerMinute` | `120` | Per-caller token-bucket limit (keyed by `sub`, else IP) |
| `OTEL_EXPORTER_OTLP_ENDPOINT` | *(unset)* | OTLP endpoint; OpenTelemetry export is a no-op when unset |

## Database & schema

One Postgres database holds both stacks, separated by schema:

| Schema | Stack | Contents |
|---|---|---|
| `cards` | EF Core | `card_printings`, `sets`, `set_type_weights` (Scryfall reference data) |
| `auth` | EF Core | `devices` (hashed device tokens) |
| `users` | Marten | collections, selections, user profiles (documents) |
| `diagnostics` | Marten | scan logs (engineering-only) |

EF Core entities, `DbContext`, and migrations live in the **Catalog** project. Run `dotnet ef` with
the host as the startup project:

```bash
dotnet ef migrations add <Name> \
  --project src/LupiraMtgApi.Catalog --startup-project src/LupiraMtgApi
```

**Applying schema in production.** Production sets Marten to `AutoCreate.None` and does **not**
migrate on boot. Apply EF migrations + Marten schema in one shot, then exit:

```bash
dotnet run --project src/LupiraMtgApi -- --apply-schema
```

Development auto-applies both on startup, so you don't run this locally.

## Docker / Compose

Build the image from the included [`Dockerfile`](Dockerfile):

```bash
docker build -t lupira-mtg-api .
```

> The runtime image installs `libfontconfig1` — SkiaSharp's native init probes fontconfig even for
> SVGs without fonts, and the set-icon rasterizer fails without it. Keep it if you slim the image.

[`deploy/compose.yaml`](deploy/compose.yaml) is a working Compose definition. Every host, port,
bucket and endpoint in it is an **overridable `${VAR:-default}` sample** — the defaults reflect the
maintainer's own deployment; set the env vars from the [Configuration](#configuration) table for
yours. The required secrets (`*_required` markers in the file) must be provided.

```bash
LUPIRA_MTG_DB_PASSWORD=… FLORENCE_API_KEY=… MINIO_ACCESS_KEY=… MINIO_SECRET_KEY=… \
  docker compose -f deploy/compose.yaml up -d
```

## Health

| Probe | Checks |
|---|---|
| `GET /livez` | Process is up (no dependencies touched) |
| `GET /readyz` | Postgres reachable (a `select 1` round-trip, 3s timeout) |

Outside Production both return a detailed per-check JSON body; in Production they return the minimal
plaintext status (the topology is not exposed on anonymous probes).

## CI

GitHub Actions ([`.github/workflows`](.github/workflows)):

- **`ci.yml`** — restore / build / test on every pull request and non-`main` push.
- **`release.yml`** — on push to `main` (and `v*` tags) re-runs CI, then builds and pushes a
  multi-tagged Docker image to Docker Hub.

## Project layout

Three bounded-context class libraries (each `Domain` / `Application` / `Infrastructure` / `Data` /
`Dtos` / `Mappers`, **no ASP.NET — compiler-enforced**) composed by a thin ASP.NET host. The heavy
computer-vision dependencies are isolated inside the Recognition context.

```
src/
  LupiraMtgApi.Catalog/      # base context: EF Core (cards/auth), Scryfall source, image storage
  LupiraMtgApi.Recognition/  # scan/detection engine (pHash, OCR, set-symbol) — depends on Catalog
  LupiraMtgApi.Collections/  # user state in Marten (users schema) — depends on Catalog
  LupiraMtgApi/              # thin host: Program.cs, Endpoints/, Handlers/, Auth/, Sync/
tests/
  LupiraMtgApi.Tests/        # xUnit
deploy/compose.yaml · Dockerfile · openapi/LupiraMtgApi.json
```

Dependency direction is enforced by project references: `Collections → Catalog`,
`Recognition → Catalog`, `host → all three`. The cross-context Scryfall sync (writes Catalog data,
rebuilds Recognition indexes) lives in the host. See [docs/architecture.md](docs/architecture.md)
for the domain model, ownership/identity model, and error-to-transport mapping.

## License

[MIT](LICENSE) © 2026 Daniel Broström.
