namespace Pragmatic.Incidents;

/// <summary>
///     The reporting windows, measured from when the incident was detected.
/// </summary>
/// <param name="EarlyWarning">First obligation — 24 hours by default.</param>
/// <param name="Notification">Full notification — 72 hours by default.</param>
/// <param name="FinalReport">Final report — one month by default.</param>
/// <remarks>
///     Configurable because the periods differ by regime and by sector, and hard-coding one regulator's
///     numbers into a framework would be quietly wrong everywhere else. The defaults are the NIS2 ones.
/// </remarks>
public sealed record IncidentDeadlines(
    TimeSpan EarlyWarning,
    TimeSpan Notification,
    TimeSpan FinalReport)
{
    /// <summary>The NIS2 windows: 24 hours, 72 hours, one month.</summary>
    public static IncidentDeadlines Nis2 { get; } = new(
        TimeSpan.FromHours(24),
        TimeSpan.FromHours(72),
        TimeSpan.FromDays(30));
}
