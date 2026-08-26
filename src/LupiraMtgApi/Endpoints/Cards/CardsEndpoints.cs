using LupiraMtgApi.Dtos.Cards;
using LupiraMtgApi.Handlers;

namespace LupiraMtgApi.Endpoints.Cards;

public static class CardsEndpoints
{
    public static IEndpointRouteBuilder MapCards(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/cards")
            .RequireAuthorization()
            .WithTags("Cards");

        group.MapGet("/", (
                [AsParameters] CardListQuery query,
                CardCatalogHandler handler,
                CancellationToken ct) =>
            handler.ListCardsAsync(query, ct))
            .WithSummary("List or search functionally distinct cards (one entry per Oracle ID).")
            .WithDescription(
                """
                Returns one row per Oracle ID — i.e. functionally distinct cards. Each row carries
                oracle-level fields (name, type line, oracle text, color identity, mana cost, CMC)
                plus a representative thumbnail picked deterministically (English, latest non-foil
                printing with an image).

                **Filters** (all optional, AND'd together):
                - `q` — fuzzy match on card name (Postgres `pg_trgm`).
                - `set` — keep only cards with a printing in this set code (e.g. `m21`).
                - `rarity` — `common|uncommon|rare|mythic|special`.
                - `color` — single color identity letter (`W|U|B|R|G`).
                - `colors` — comma-separated multi-color identity (AND), e.g. `colors=W,U` keeps
                  only cards whose color identity contains every listed letter.
                - `type` — fuzzy match on the recomposed type line (e.g. `type=goblin warrior`).
                - `cmc` — exact converted mana cost.
                - `cmcMin` / `cmcMax` — CMC range bounds (inclusive).
                - `power` / `toughness` — exact match (strings, since `*` and `1+1` are valid).

                **Sort**: `sort=name|cmc|releasedAt|rarity|relevance`. Defaults to `relevance` when
                `q` is provided, otherwise `name`. `order=asc|desc` (relevance is always best-first).

                **Pagination**: `take` 1–100 (default 25), `skip` for paging.

                Use `GET /cards/{oracleId}/printings` to drill into printings for a specific card.
                """)
            .Produces<CardListResponse>(StatusCodes.Status200OK)
            .WithName("ListCards");

        group.MapGet("/{oracleId}", (
                string oracleId,
                CardCatalogHandler handler,
                CancellationToken ct) =>
            handler.GetCardAsync(oracleId, ct))
            .WithSummary("Get a single card (oracle-level) by Oracle ID.")
            .WithDescription(
                """
                Returns the oracle-level view of a card: name, type line, oracle text, color identity,
                and a representative thumbnail. Does **not** return printing-specific data such as set,
                collector number, or prices — see `GET /cards/{oracleId}/printings`.
                """)
            .Produces<CardDto>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .WithName("GetCard");

        group.MapGet("/{oracleId}/printings", (
                string oracleId,
                CardCatalogHandler handler,
                CancellationToken ct) =>
            handler.ListPrintingsAsync(oracleId, ct))
            .WithSummary("List every printing of a card.")
            .WithDescription(
                """
                Returns every printing that shares this Oracle ID, ordered newest-set first.
                Bounded list (no pagination) — even the most-reprinted cards have ~100 printings.
                """)
            .Produces<IReadOnlyList<CardPrintingDto>>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .WithName("ListPrintings");

        group.MapGet("/{oracleId}/printings/{printingId}", (
                string oracleId,
                string printingId,
                CardCatalogHandler handler,
                CancellationToken ct) =>
            handler.GetPrintingAsync(oracleId, printingId, ct))
            .WithSummary("Get a single card printing by Scryfall ID.")
            .WithDescription(
                """
                Returns a printing's metadata along with presigned URLs for the normal-size image
                and the art crop (if present in the local image store). Returns 404 if the printing
                is unknown to the local catalog or if `oracleId` does not match the printing's
                Oracle ID (re-run sync if it should exist).
                """)
            .Produces<CardPrintingDto>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .WithName("GetPrinting");

        group.MapGet("/{oracleId}/printings/{printingId}/prices", (
                string oracleId,
                string printingId,
                DateOnly? from,
                DateOnly? to,
                CardPriceHistoryHandler handler,
                CancellationToken ct) =>
            handler.GetHistoryAsync(printingId, from, to, ct))
            .WithSummary("Get the EUR price history of a printing.")
            .WithDescription(
                """
                Returns the daily price history for a printing (store-on-change: a point exists only
                for days the price moved), oldest first, clamped to the configured retention window.
                Optional `from`/`to` query params (`yyyy-MM-dd`) narrow the range. Returns 404 when no
                price points have been recorded for the printing.
                """)
            .Produces<CardPriceHistoryResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .WithName("ListPrices");

        return app;
    }
}
