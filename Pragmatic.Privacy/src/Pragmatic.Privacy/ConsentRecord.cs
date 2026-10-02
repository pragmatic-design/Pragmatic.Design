namespace Pragmatic.Privacy;

/// <summary>
///     A subject's consent to one processing purpose, under one version of the notice they were shown.
/// </summary>
/// <remarks>
///     <para>
///         <b>Consent is per notice version, not a boolean.</b> "The subject consented" is not a
///         defensible answer on its own — what matters is what they were told at the time. Change the
///         notice and the old consent no longer covers the new processing, which is exactly the fact a
///         single flag destroys.
///     </para>
///     <para>
///         Withdrawal does not delete the record. Proving that consent existed, and when it stopped, is
///         the point: a deleted consent record and a consent that was never given look identical, and
///         one of them is a breach.
///     </para>
/// </remarks>
public sealed class ConsentRecord
{
    /// <summary>The subject's opaque reference. Never their identity.</summary>
    public required string SubjectRef { get; set; }

    /// <summary>What the consent is for. One purpose per record — bundled consent is not consent.</summary>
    public required string Purpose { get; set; }

    /// <summary>The version of the notice the subject was shown when they agreed.</summary>
    public required string NoticeVersion { get; set; }

    /// <summary>When it was given.</summary>
    public DateTimeOffset GrantedAt { get; set; }

    /// <summary>When it was withdrawn; null while it still stands.</summary>
    public DateTimeOffset? WithdrawnAt { get; set; }

    /// <summary>
    ///     How the consent was captured — a form, an API call, a signed document.
    /// </summary>
    /// <remarks>
    ///     Recorded because the burden of proof sits with the controller: being unable to say how
    ///     consent was obtained is, in practice, being unable to show it was.
    /// </remarks>
    public string? Source { get; set; }

    /// <summary>True while the consent stands.</summary>
    public bool IsActive => WithdrawnAt is null;

    /// <summary>Whether this record covers the given purpose under the given notice version.</summary>
    public bool Covers(string purpose, string noticeVersion)
        => IsActive
           && string.Equals(Purpose, purpose, StringComparison.Ordinal)
           && string.Equals(NoticeVersion, noticeVersion, StringComparison.Ordinal);
}
