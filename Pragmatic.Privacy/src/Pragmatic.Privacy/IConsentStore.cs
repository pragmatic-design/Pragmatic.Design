namespace Pragmatic.Privacy;

/// <summary>
///     Records what a subject has agreed to, and what they have withdrawn.
/// </summary>
public interface IConsentStore
{
    /// <summary>Records consent to one purpose under one notice version.</summary>
    ValueTask GrantAsync(
        string subjectRef, string purpose, string noticeVersion, string? source = null,
        CancellationToken ct = default);

    /// <summary>
    ///     Withdraws consent to a purpose.
    /// </summary>
    /// <remarks>
    ///     <b>Withdrawal is not an erasure request.</b> It stops processing that relied on consent; data
    ///     held on another lawful basis — a contract, a fiscal obligation — is unaffected. Treating the
    ///     two as the same thing would either delete records the controller must keep, or quietly ignore
    ///     a withdrawal.
    /// </remarks>
    /// <returns>False when there was no active consent to withdraw.</returns>
    ValueTask<bool> WithdrawAsync(
        string subjectRef, string purpose, DateTimeOffset now, CancellationToken ct = default);

    /// <summary>
    ///     Whether processing for this purpose is currently covered, under the notice version in force.
    /// </summary>
    /// <remarks>
    ///     The notice version is a parameter rather than an implicit "latest": asking whether consent
    ///     covers <em>the processing about to happen</em> is the only question with a useful answer.
    /// </remarks>
    ValueTask<bool> IsGrantedAsync(
        string subjectRef, string purpose, string noticeVersion, CancellationToken ct = default);

    /// <summary>Every consent record for a subject, withdrawn ones included — an access request needs them.</summary>
    ValueTask<IReadOnlyList<ConsentRecord>> GetHistoryAsync(
        string subjectRef, CancellationToken ct = default);
}
