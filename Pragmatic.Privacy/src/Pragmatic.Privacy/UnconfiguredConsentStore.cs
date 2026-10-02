namespace Pragmatic.Privacy;

/// <summary>
///     The <see cref="IConsentStore" /> an application gets when it has not chosen one: it records
///     nothing and says so.
/// </summary>
/// <remarks>
///     <para>
///         Same reasoning as <see cref="UnconfiguredSubjectRegistry" />, and the same reason it throws
///         instead of answering plausibly. <c>IsGrantedAsync</c> returning false would look fail-closed
///         and would in fact be a store that had silently discarded every grant; <c>GrantAsync</c>
///         accepting a consent it does not keep is worse still, because the record an authority asks for
///         is the one that never existed.
///     </para>
///     <para>
///         Registered by <c>AddPrivacy()</c> with <c>TryAdd</c>, so any real store — including the EF
///         Core one from <c>AddSubjectRegistry()</c> — replaces it.
///     </para>
/// </remarks>
internal sealed class UnconfiguredConsentStore : IConsentStore
{
    private const string Explanation =
        "No IConsentStore is registered, so consent cannot be recorded or checked. Call " +
        "AddSubjectRegistry() from Pragmatic.Privacy.EFCore, which registers one over PrivacyDbContext, " +
        "or register your own implementation. The Article 30 register does not use this and is " +
        "unaffected.";

    public ValueTask GrantAsync(
        string subjectRef, string purpose, string noticeVersion, string? source = null,
        CancellationToken ct = default)
        => throw new InvalidOperationException(Explanation);

    public ValueTask<bool> WithdrawAsync(
        string subjectRef, string purpose, DateTimeOffset now, CancellationToken ct = default)
        => throw new InvalidOperationException(Explanation);

    public ValueTask<bool> IsGrantedAsync(
        string subjectRef, string purpose, string noticeVersion, CancellationToken ct = default)
        => throw new InvalidOperationException(Explanation);

    public ValueTask<IReadOnlyList<ConsentRecord>> GetHistoryAsync(
        string subjectRef, CancellationToken ct = default)
        => throw new InvalidOperationException(Explanation);
}
