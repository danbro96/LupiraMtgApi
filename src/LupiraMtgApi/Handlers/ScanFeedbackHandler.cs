using LupiraMtgApi.Data;
using LupiraMtgApi.Domain.ScanLog;
using LupiraMtgApi.Models.Scans;
using LupiraMtgApi.Services.Recognition.Pipeline;
using Marten;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace LupiraMtgApi.Handlers;

/// <summary>
/// Records what the user actually wanted from a scan and reports back where that
/// printing ranked in the candidate pool the API surfaced. Drives both the in-app
/// "we missed it by N positions" UX and the eventual ranker training corpus.
/// Returns 404 (not 403) for scans owned by other users so we don't leak existence.
/// </summary>
public sealed class ScanFeedbackHandler
{
    private readonly IDocumentSession _session;
    private readonly LupiraMtgDbContext _db;

    public ScanFeedbackHandler(IDocumentSession session, LupiraMtgDbContext db)
    {
        _session = session;
        _db = db;
    }

    public async Task<Results<Ok<ScanFeedbackResponse>, NotFound, BadRequest<string>, UnauthorizedHttpResult>> SubmitAsync(
        HttpContext httpContext,
        Guid scanId,
        ScanFeedbackRequest request,
        CancellationToken ct)
    {
        if (!httpContext.TryGetOwnerId(out var ownerId))
        {
            return TypedResults.Unauthorized();
        }

        if (string.IsNullOrWhiteSpace(request.CorrectPrintingId))
        {
            return TypedResults.BadRequest("correctPrintingId is required.");
        }

        var printingId = request.CorrectPrintingId.Trim();

        using var span = ScanTelemetry.Source.StartActivity("scan.feedback.submit");
        span?.SetTag("scan.id", scanId);
        span?.SetTag("feedback.printing_id", printingId);

        var scan = await _session.LoadAsync<ScanLogDocument>(scanId, ct);
        if (scan is null || scan.OwnerId != ownerId)
        {
            return TypedResults.NotFound();
        }

        var printingExists = await EntityFrameworkQueryableExtensions.AnyAsync(
            _db.CardPrintings.AsNoTracking(),
            p => p.Id == printingId,
            ct);
        if (!printingExists)
        {
            return TypedResults.BadRequest("Unknown printing id.");
        }

        int? rank = null;
        for (var i = 0; i < scan.Candidates.Count; i++)
        {
            if (string.Equals(scan.Candidates[i].PrintingId, printingId, StringComparison.Ordinal))
            {
                rank = i + 1;
                break;
            }
        }

        var overwrite = scan.FeedbackAt is not null;
        scan.FeedbackCorrectPrintingId = printingId;
        scan.FeedbackCorrectPrintingRank = rank;
        scan.FeedbackAt = DateTimeOffset.UtcNow;

        _session.Store(scan);
        await _session.SaveChangesAsync(ct);

        span?.SetTag("feedback.candidate_count", scan.Candidates.Count);
        span?.SetTag("feedback.overwrite", overwrite);
        if (rank is int r)
        {
            span?.SetTag("feedback.rank", r);
        }
        else
        {
            span?.SetTag("feedback.not_in_pool", true);
        }

        return TypedResults.Ok(new ScanFeedbackResponse
        {
            ScanId = scanId,
            CorrectPrintingId = printingId,
            Rank = rank,
            CandidateCount = scan.Candidates.Count,
        });
    }
}
