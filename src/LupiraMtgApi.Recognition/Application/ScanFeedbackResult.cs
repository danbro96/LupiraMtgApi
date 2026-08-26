using LupiraMtgApi.Recognition.Dtos;

namespace LupiraMtgApi.Recognition.Application;

/// <summary>
/// Result of submitting scan feedback. The host adapter maps <see cref="Status"/> onto HTTP:
/// <c>ScanNotFound</c> → 404 (also covers other-owner scans so existence isn't leaked),
/// <c>UnknownPrinting</c> → 400, <c>Ok</c> → 200 with <see cref="Response"/>.
/// </summary>
public sealed record ScanFeedbackResult(ScanFeedbackStatus Status, ScanFeedbackResponse? Response);
