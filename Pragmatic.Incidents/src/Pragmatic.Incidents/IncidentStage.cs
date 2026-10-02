namespace Pragmatic.Incidents;

/// <summary>
///     Where a security incident has got to in its reporting obligations.
/// </summary>
public enum IncidentStage
{
    /// <summary>Something was noticed. The clocks start here.</summary>
    Detected = 0,

    /// <summary>
    ///     Assessed as significant, or not.
    /// </summary>
    /// <remarks>
    ///     <b>The framework never makes this call.</b> Whether an incident is notifiable is a judgement
    ///     about impact, and a system that decided it automatically would be wrong in both directions:
    ///     reporting noise, and staying silent about the one that mattered.
    /// </remarks>
    Assessed = 1,

    /// <summary>Early warning sent — the first obligation, within 24 hours.</summary>
    EarlyWarningSent = 2,

    /// <summary>Full notification sent — within 72 hours.</summary>
    NotificationSent = 3,

    /// <summary>Final report filed — within one month.</summary>
    FinalReportFiled = 4,

    /// <summary>Assessed as not notifiable, and closed. Still recorded: deciding not to report is a decision.</summary>
    ClosedNotNotifiable = 5
}
