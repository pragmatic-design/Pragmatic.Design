namespace Pragmatic.Privacy;

/// <summary>
///     Resolves an identity that merely <em>appeared</em> — in a failed login, a lookup, a webhook — to
///     a subject reference, but only when that subject already exists.
/// </summary>
/// <remarks>
///     <para>
///         <b>Never allocates.</b> The obvious way to record a failed login is to pseudonymise the
///         address that was tried, which means calling
///         <see cref="ISubjectRegistry.GetOrCreateReferenceAsync" /> on a string a stranger typed. That
///         turns every failed attempt into a new row in the subject registry: an attacker can fill it at
///         will, and worse, the system starts holding records about people who never had a relationship
///         with it — creating personal data as a side effect of rejecting them.
///     </para>
///     <para>
///         So: an attempt against a known account carries that account's reference and can be correlated
///         with the rest of its history; an attempt against something nobody recognises is recorded
///         <b>without a subject at all</b>. Losing the ability to group attempts on non-existent accounts
///         is a real cost, and a smaller one than the alternative.
///     </para>
/// </remarks>
public sealed class ObservedIdentityResolver(ISubjectRegistry registry)
{
    /// <summary>
    ///     Returns the subject reference for an identity that has been observed, or
    ///     <see langword="null" /> when no such subject exists.
    /// </summary>
    /// <remarks>
    ///     A null result is the normal case for an attempt against an unknown account, and callers
    ///     should record the event anyway — an unattributable failure is still a security signal, and
    ///     often the more interesting one.
    /// </remarks>
    public async ValueTask<string?> ResolveAsync(
        string subjectType, string observedIdentity, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(subjectType) || string.IsNullOrWhiteSpace(observedIdentity))
            return null;

        return await registry.FindReferenceAsync(subjectType, observedIdentity, ct).ConfigureAwait(false);
    }
}
