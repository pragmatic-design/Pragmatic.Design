namespace Pragmatic.Internationalization.Humanizer;

/// <summary>
///     Short suffixes for duration formatting.
/// </summary>
internal readonly record struct DurationSuffixes(
    string Days,
    string Hours,
    string Minutes,
    string Seconds,
    string Milliseconds);
