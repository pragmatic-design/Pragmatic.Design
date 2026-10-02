namespace Pragmatic.Internationalization.Humanizer;

/// <summary>
///     Suffixes for quantity abbreviations.
/// </summary>
/// <param name="Thousand">Suffix for thousands (e.g., "K").</param>
/// <param name="Million">Suffix for millions (e.g., "M").</param>
/// <param name="Billion">Suffix for billions (e.g., "B").</param>
/// <param name="Trillion">Suffix for trillions (e.g., "T").</param>
public readonly record struct QuantitySuffixes(
    string Thousand,
    string Million,
    string Billion,
    string Trillion);
