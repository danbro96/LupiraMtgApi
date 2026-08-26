namespace LupiraMtgApi.Recognition.Application;

/// <summary>Outcome of <see cref="ScanFeedbackService.SubmitAsync"/>.</summary>
public enum ScanFeedbackStatus
{
    Ok,
    ScanNotFound,
    UnknownPrinting,
}
