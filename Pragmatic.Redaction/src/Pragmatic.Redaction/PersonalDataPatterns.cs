using System.Text.RegularExpressions;

namespace Pragmatic.Redaction;

/// <summary>
///     The personal-data shapes worth recognising in free text, in one place.
/// </summary>
/// <remarks>
///     <para>
///         One set, because two subsystems each carrying their own drift apart: one lets a national
///         identifier or a card-verification value through in plaintext while the other catches both.
///         The audit trail is append-only and kept for years, so its floor must never be the lower of
///         the two.
///     </para>
///     <para>
///         <b>This is a floor, not a guarantee.</b> Pattern matching cannot recognise a name, an
///         address, or a sentence about someone's health. A caller that puts a personal value into free
///         text is relying on a net with holes; the point is that the obvious shapes do not get through.
///     </para>
///     <para>
///         <b>Deliberately absent: dates.</b> A date is personal data only in context — a birth date is,
///         "locked until 2026-08-01" is not — and a pattern cannot tell them apart. Redacting every date
///         would empty the one field that explains why an entry exists, which is how a redactor stops
///         being used at all.
///     </para>
/// </remarks>
public static partial class PersonalDataPatterns
{
    /// <summary>What a redacted value is replaced with.</summary>
    public const string Mask = global::Pragmatic.Serialization.RedactionMask.Value;

    /// <summary>
    ///     The pattern strings, for callers that compile their own.
    /// </summary>
    /// <remarks>
    ///     <c>Pragmatic.Logging</c> builds its compliance presets from configurable string lists rather
    ///     than compiled regexes, so it needs the source text. Taken from the compiled instances instead
    ///     of being written twice — a second copy of a regex literal is exactly the drift this package
    ///     exists to remove.
    /// </remarks>
    public static class Text
    {
        /// <inheritdoc cref="PersonalDataPatterns.Email" />
        public static string Email => PersonalDataPatterns.Email().ToString();

        /// <inheritdoc cref="PersonalDataPatterns.Iban" />
        public static string Iban => PersonalDataPatterns.Iban().ToString();

        /// <inheritdoc cref="PersonalDataPatterns.NationalIdentifier" />
        public static string NationalIdentifier => PersonalDataPatterns.NationalIdentifier().ToString();

        /// <inheritdoc cref="PersonalDataPatterns.CardVerificationValue" />
        public static string CardVerificationValue => PersonalDataPatterns.CardVerificationValue().ToString();
    }

    /// <summary>E-mail addresses.</summary>
    [GeneratedRegex(@"[\w.+-]+@[\w-]+\.[\w.-]+", RegexOptions.IgnoreCase)]
    public static partial Regex Email();

    /// <summary>Bearer tokens, mask kept alongside the scheme so the shape stays readable.</summary>
    [GeneratedRegex(@"Bearer\s+[A-Za-z0-9\-._~+/]+=*", RegexOptions.IgnoreCase)]
    public static partial Regex BearerToken();

    /// <summary>
    ///     A long run of digits — payment cards, phone numbers, account numbers.
    /// </summary>
    /// <remarks>
    ///     Broad on purpose. Separate patterns per instrument would each miss their own formatting
    ///     variants, and a value long enough to be one of these is rarely something worth keeping.
    /// </remarks>
    [GeneratedRegex(@"\b\d[\d ._-]{10,}\d\b")]
    public static partial Regex LongDigitRun();

    /// <summary>IBANs.</summary>
    [GeneratedRegex(@"\b[A-Z]{2}\d{2}[A-Z0-9]{10,30}\b")]
    public static partial Regex Iban();

    /// <summary>
    ///     National identifiers in the ddd-dd-dddd shape.
    /// </summary>
    /// <remarks>
    ///     Too short for <see cref="LongDigitRun" /> to see, so without its own pattern it would reach
    ///     the audit trail in plaintext.
    /// </remarks>
    [GeneratedRegex(@"\b\d{3}-\d{2}-\d{4}\b")]
    public static partial Regex NationalIdentifier();

    /// <summary>
    ///     Card verification values, matched by their label.
    /// </summary>
    /// <remarks>
    ///     Three or four digits are unrecognisable on their own; the label is the only signal. Storing
    ///     one is forbidden outright by PCI DSS, which makes letting it through worse than most leaks.
    /// </remarks>
    [GeneratedRegex(@"(?i)\b(cvv|cvc|cvv2|cvc2|cid|security\s*code|card\s*verification(\s*(value|code))?)\b\s*[:=]?\s*\d{3,4}")]
    public static partial Regex CardVerificationValue();

    /// <summary>A secret assigned in <c>key=value</c> or <c>key: value</c> form.</summary>
    [GeneratedRegex(@"(?i)\b(password|pwd|secret|token|api[_-]?key|apikey|authorization)\b\s*[:=]\s*\S+")]
    public static partial Regex KeyValueSecret();
}
