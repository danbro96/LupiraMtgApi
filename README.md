# LupiraMtgApi

Backend for the Lupira MTG mobile app — Magic: The Gathering card metadata, scan-based recognition, and per-user collection management. Self-hosted on TrueNAS, deployed alongside KokoroApi/FlorenceApi/LupiraWeb.

> **Status:** Phase 1 skeleton — `/healthz`, OpenAPI, Scalar UI, OIDC, Marten + EF Core wired. No business endpoints yet. See the architecture plan in `KokoroApi/.claude/plans/i-want-to-plan-modular-taco.md` for the roadmap.

## Stack

- .NET 10 minimal API
- Marten 8 (event-sourced collections, selections, user profiles in `public` schema)
- EF Core 10 + Npgsql (Scryfall card catalog in `cards` schema)
- OpenAPI + Scalar UI at `/scalar`
- OpenTelemetry → OpenObserve via OTLP
- Authentik OIDC bearer auth (multi-user)

Both Marten and EF Core share the same Postgres connection (database `lupira_mtg` on `medelynas-db`), separated by schema.

## Run locally

You need a Postgres reachable at the connection string in `appsettings.Development.json` (or override via env). Empty DB is fine — Marten creates `public` schema lazily; run EF migrations once they exist (Phase 2):

```bash
dotnet restore src/LupiraMtgApi/LupiraMtgApi.csproj
dotnet run --project src/LupiraMtgApi/LupiraMtgApi.csproj
# → http://localhost:8080/scalar
# → http://localhost:8080/healthz
```

In dev, OIDC validation is effectively bypassed when `Auth:Authority` is empty — Bearer tokens still parse, but issuer/audience checks are skipped. Production must always set both.

## Deploy

See [`deploy/compose.yaml`](deploy/compose.yaml) and the runbook at `DevOps/Websites/lupira-mtg-api/deployment.md`.

## Layout

```
src/LupiraMtgApi/
  Program.cs                       # composition root
  Auth/OidcSetup.cs                # JwtBearer wiring against Authentik
  Endpoints/                       # thin route declarations (REST verbs + OpenAPI)
  Handlers/                        # request handlers (logic; called from endpoints)
  Models/                          # request/response DTOs
  Services/                        # FlorenceClient, MinioClient, PHashService, ScryfallClient (Phase 2+)
  Domain/
    Collection/                    # Marten event-sourced aggregate
    Selection/                     # Marten document (ephemeral)
    UserProfile/                   # Marten document
  Data/
    LupiraMtgDbContext.cs          # EF DbContext (cards schema)
    Entities/                      # CardPrinting, ScryfallSet
    Migrations/                    # EF migrations (added in Phase 2)
  Jobs/                            # ScryfallSyncJob (Phase 2)
  MartenRegistrations.cs           # document/projection registration
  appsettings.json
  appsettings.Development.json
deploy/compose.yaml
Dockerfile
```
