using LupiraMtgApi.Handlers;

namespace LupiraMtgApi.Endpoints.Cards;

public static class SearchEndpoint
{
    public static IEndpointConventionBuilder MapCardSearch(this IEndpointRouteBuilder app) =>
        app.MapGet("/cards/search", (
                string? q,
                string? set,
                string? color,
                string? rarity,
                int? limit,
                CardSearchHandler handler,
                CancellationToken ct) =>
            handler.SearchAsync(q, set, color, rarity, limit, ct))
            .WithTags("Cards")
            .WithSummary("Fuzzy-search the local Scryfall card catalog.")
            .WithDescription(
                """
                `q` is fuzzy-matched against printing names via Postgres `pg_trgm`.
                `set` filters by lower-case set code (e.g. `m21`).
                `color` filters by single color identity letter (`W|U|B|R|G`).
                `rarity` filters by `common|uncommon|rare|mythic|special`.
                `limit` caps results (default 25, max 100).
                """);
}
