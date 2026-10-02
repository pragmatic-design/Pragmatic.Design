namespace Pragmatic.Internationalization.Humanizer;

/// <summary>
///     Duration output format.
/// </summary>
public enum DurationFormat
{
    /// <summary>Short format with abbreviated suffixes (e.g., "2h 30m").</summary>
    Short,

    /// <summary>Long format with full words (e.g., "2 hours 30 minutes").</summary>
    Long,

    /// <summary>Compact colon-separated format (e.g., "2:30:00").</summary>
    Compact
}
