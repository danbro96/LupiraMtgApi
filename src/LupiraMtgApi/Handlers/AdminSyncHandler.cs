using LupiraMtgApi.Dtos.Sync;
using LupiraMtgApi.Workers;
using Microsoft.AspNetCore.Http.HttpResults;

namespace LupiraMtgApi.Handlers;

public sealed class AdminSyncHandler
{
    private readonly ScryfallSyncRunner _runner;

    public AdminSyncHandler(ScryfallSyncRunner runner)
    {
        _runner = runner;
    }

    public async Task<Ok<SyncRunResponse>> RunAsync(CancellationToken ct)
    {
        var result = await _runner.RunAsync(ct);
        return TypedResults.Ok(result);
    }
}
