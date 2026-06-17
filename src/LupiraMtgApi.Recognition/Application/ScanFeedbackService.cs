using LupiraMtgApi.Catalog.Data;
using LupiraMtgApi.Recognition.Application.Pipeline;
using LupiraMtgApi.Recognition.Dtos;
using Marten;
using Microsoft.EntityFrameworkCore;

namespace LupiraMtgApi.Recognition.Application;

/// <summary>Outcome of <see cref="ScanFeedbackService.SubmitAsync"/>.</summary>
public enum ScanFeedbackStatus
{
    Ok,
    ScanNotFound,
    UnknownPrinting,
}

/// <summary>
/// Result of submitting scan feedback. The host adapter maps <see cref="Status"/> onto HTTP:
/// <c>ScanNotFound</c> → 404 (also covers other-owner scans so existence isn't leaked),
/// <c>UnknownPrinting</c> → 400, <c>Ok</c> → 200 with <see cref="Response"/>.
/// </summary>
public sealed record ScanFeedbackResult(ScanFeedbackStatus Status, ScanFeedbackResponse? Response);

/// <summary>
/// Records what the user actually wanted from a scan and reports where that printing ranked in the
/// candidate pool the API surfaced. Owner identity is resolved by the host adapter and passed in;
/// the empty-id check is transport validation done before this call.
/// </summary>
public sealed class ScanFeedbackService
{
    private readonly IDocumentSession _session;
    private readonly LupiraMtgDbContext _db;

    public ScanFeedbackService(IDocumentSession session, LupiraMtgDbContext db)
    {
        _session = session;
        _db = db;
    }

    public async Task<ScanFeedbackResult> SubmitAsync(
        Guid ownerId,
        Guid scanId,
        string correctPrintingId,
        CancellationToken ct)
    {
        var printingId = correctPrintingId.Trim();

        using var span = ScanTelemetry.Source.StartActivity("scan.feedback.submit");
        span?.SetTag("scan.id", scanId);
        span?.SetTag("feedback.printing_id", printingId);

        var scan = await _session.LoadAsync<ScanLogDocument>(scanId, ct);
        if (scan is null || scan.OwnerId != ownerId)
        {
            return new ScanFeedbackResult(ScanFeedbackStatus.ScanNotFound, null);
        }

        var printingExists = await EntityFrameworkQueryableExtensions.AnyAsync(
            _db.CardPrintings.AsNoTracking(),
            p => p.Id == printingId,
            ct);
        if (!printingExists)
        {
            return new ScanFeedbackResult(ScanFeedbackStatus.UnknownPrinting, null);
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

        return new ScanFeedbackResult(ScanFeedbackStatus.Ok, new ScanFeedbackResponse
        {
            ScanId = scanId,
            CorrectPrintingId = printingId,
            Rank = rank,
            CandidateCount = scan.Candidates.Count,
        });
    }
}
