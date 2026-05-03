using LupiraMtgApi.Jobs;
using LupiraMtgApi.Models;
using Microsoft.AspNetCore.Http.HttpResults;

namespace LupiraMtgApi.Handlers;

public sealed class AdminSyncHandler
{
    private readonly ScryfallSyncRunner runner;

    public AdminSyncHandler(ScryfallSyncRunner runner)
    {
        this.runner = runner;
    }

    public async Task<Ok<SyncRunResponse>> RunAsync(CancellationToken ct)
    {
        var result = await this.runner.RunAsync(ct);
        return TypedResults.Ok(result);
    }
}
