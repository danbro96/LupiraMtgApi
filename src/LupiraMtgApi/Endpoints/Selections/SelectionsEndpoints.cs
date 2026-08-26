using LupiraMtgApi.Handlers;

namespace LupiraMtgApi.Endpoints.Selections;

public static class SelectionsEndpoints
{
    public static IEndpointRouteBuilder MapSelections(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/selections")
            .RequireAuthorization()
            .WithTags("Selections");

        group.MapPost("/", (HttpContext ctx, SelectionsHandler h, CancellationToken ct) => h.CreateAsync(ctx, ct))
            .WithSummary("Start a new scanning selection.")
            .Produces<SelectionResponse>(StatusCodes.Status200OK)
            .WithName("CreateSelection");

        group.MapGet("/{selectionId:guid}", (HttpContext ctx, Guid selectionId, SelectionsHandler h, CancellationToken ct) =>
                h.GetAsync(ctx, selectionId, ct))
            .WithSummary("Get a selection with its current cards.")
            .Produces<SelectionResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .WithName("GetSelection");

        group.MapPost("/{selectionId:guid}/cards", (HttpContext ctx, Guid selectionId, AddSelectionEntryRequest body, SelectionsHandler h, CancellationToken ct) =>
                h.AddCardAsync(ctx, selectionId, body, ct))
            .WithSummary("Add a recognized card to the selection. Returns 409 on duplicate unless allowDuplicate=true.")
            .Produces<SelectionEntryDto>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .WithName("CreateSelectionCard");

        group.MapDelete("/{selectionId:guid}/cards/{instanceId:guid}", (HttpContext ctx, Guid selectionId, Guid instanceId, SelectionsHandler h, CancellationToken ct) =>
                h.RemoveCardAsync(ctx, selectionId, instanceId, ct))
            .WithSummary("Remove a card from the selection.")
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .WithName("DeleteSelectionCard");

        group.MapPost("/{selectionId:guid}/commit", (HttpContext ctx, Guid selectionId, CommitSelectionRequest body, SelectionsHandler h, CancellationToken ct) =>
                h.CommitAsync(ctx, selectionId, body, ct))
            .WithSummary("Commit selection cards into a collection.")
            .Produces<CommitSelectionResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .WithName("CommitSelection");

        return app;
    }
}
