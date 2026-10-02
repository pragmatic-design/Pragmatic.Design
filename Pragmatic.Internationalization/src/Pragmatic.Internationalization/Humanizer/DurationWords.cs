namespace Pragmatic.Internationalization.Humanizer;

/// <summary>
///     Long word pairs (singular/plural/few) for duration formatting.
///     The Few form is used by Slavic languages for counts 2-4 (e.g., Russian "2 дня", "3 часа").
/// </summary>
internal readonly record struct DurationWords(
    (string Singular, string Plural, string Few) Days,
    (string Singular, string Plural, string Few) Hours,
    (string Singular, string Plural, string Few) Minutes,
    (string Singular, string Plural, string Few) Seconds,
    (string Singular, string Plural, string Few) Milliseconds);
