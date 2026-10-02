using Pragmatic.Redaction;

namespace Pragmatic.Audit;

/// <summary>
///     Default <see cref="IAuditDetailRedactor" />: replaces the shapes most likely to carry personal or
///     secret data out of a free-text detail.
/// </summary>
/// <remarks>
///     <para>
///         This is a floor, not a guarantee. Pattern matching cannot recognise a name, and a caller
///         determined to put personal data in a detail string will succeed. The structural defence is
///         that the entry has no general-purpose payload field at all; this covers the one field that
///         remains.
///     </para>
///     <para>
///         The patterns are <c>Pragmatic.Redaction</c>'s, a small package the logging pipeline draws
///         on too, so the trail gets them without pulling the logging module into every consumer.
///     </para>
/// </remarks>
public sealed class PatternAuditDetailRedactor : IAuditDetailRedactor
{
    /// <inheritdoc />
    /// <remarks>
    ///     Delegates to <see cref="PersonalDataRedactor" /> rather than carrying its own patterns. Two
    ///     pattern sets drift: a separate set here could let a national identifier and a
    ///     card-verification value through in plaintext while the logging pipeline catches both — the
    ///     wrong way round, since the trail is append-only and kept for years.
    /// </remarks>
    public string Redact(string detail) => PersonalDataRedactor.Redact(detail) ?? detail;
}
