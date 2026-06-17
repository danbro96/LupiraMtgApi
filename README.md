# LupiraMtgApi

Backend for the Lupira MTG mobile app — Magic: The Gathering card metadata, scan-based recognition, and per-user collection management. Self-hosted on TrueNAS, deployed alongside KokoroApi/FlorenceApi/LupiraWeb.

> **Status:** Phase 1 skeleton — `/livez` + `/readyz`, OpenAPI, Scalar UI, OIDC, Marten + EF Core wired. No business endpoints yet. See the architecture plan in `KokoroApi/.claude/plans/i-want-to-plan-modular-taco.md` for the roadmap.

## Stack

- .NET 10 minimal API
- Marten 8 (collections, selections, user profiles in `users` schema; scan logs in `diagnostics`)
- EF Core 10 + Npgsql (Scryfall card catalog in `cards` schema, device users in `auth`)
- OpenAPI + Scalar UI at `/scalar`
- OpenTelemetry → OpenObserve via OTLP
- Authentik OIDC bearer auth (multi-user)

Both Marten and EF Core share the same Postgres connection (database `lupira_mtg` on `medelynas-db`), separated by schema. The code is split into three bounded-context libraries (Catalog, Recognition, Collections) over a thin host — see [Layout](#layout).

## Run locally

You need a Postgres reachable at the connection string in `appsettings.Development.json` (or override via env). Empty DB is fine — Marten creates `public` schema lazily; run EF migrations once they exist (Phase 2):

```bash
dotnet restore src/LupiraMtgApi/LupiraMtgApi.csproj
dotnet run --project src/LupiraMtgApi/LupiraMtgApi.csproj
# → http://localhost:8080/scalar
# → http://localhost:8080/livez   (liveness)  ·  /readyz (readiness, pings Postgres)
```

In dev, OIDC validation is effectively bypassed when `Auth:Authority` is empty — Bearer tokens still parse, but issuer/audience checks are skipped. Production must always set both.

## Deploy

See [`deploy/compose.yaml`](deploy/compose.yaml) and the runbook at `DevOps/Websites/lupira-mtg-api/deployment.md`.

## Layout

Three bounded-context class libraries (Domain / Application / Infrastructure / Data / Dtos / Mappers,
**no ASP.NET — compiler-enforced**) composed by a thin ASP.NET host. The heavy CV dependencies
(SkiaSharp / ImageSharp / OCR) are isolated to the Recognition context.

```
src/
  LupiraMtgApi.Catalog/        # base context: cards/auth schema (EF Core), Scryfall source, image storage
    Domain/ Application/ Infrastructure/{Scryfall,Storage} Data/{DbContext,Migrations} Dtos/ Mappers/
  LupiraMtgApi.Recognition/    # scan/detection engine — depends on Catalog
    Domain/(scoring,ScanLog) Application/(pipeline,steps) Infrastructure/{Ocr,Imaging,SetSymbol} Data/(Marten) Dtos/
  LupiraMtgApi.Collections/    # user state (users schema, Marten) — depends on Catalog
    Domain/(docs) Application/(services) Data/(Marten) Dtos/ Mappers/(hydrator)
  LupiraMtgApi/                # thin host — depends on all three
    Program.cs Endpoints/ Handlers/(thin adapters) Auth/ Sync/(Scryfall sync orchestration) Models/
tests/
  LupiraMtgApi.Tests/          # xUnit
Directory.Build.props          # shared TFM/nullable/analyzers
deploy/compose.yaml
Dockerfile
```

Dependency direction is compiler-enforced: `Collections → Catalog`, `Recognition → Catalog`,
`host → all three`. The cross-context Scryfall sync (writes Catalog data + rebuilds Recognition
indexes) lives in the host.

### Database migrations

EF Core entities/`DbContext`/migrations live in **Catalog**; run `dotnet ef` with the host as the
startup project:

```bash
dotnet ef migrations add <Name> --project src/LupiraMtgApi.Catalog --startup-project src/LupiraMtgApi
```

Production controls schema explicitly (Marten `AutoCreate.None`); apply EF migrations + Marten schema
in one shot with `dotnet run --project src/LupiraMtgApi -- --apply-schema`. Dev auto-applies on boot.
