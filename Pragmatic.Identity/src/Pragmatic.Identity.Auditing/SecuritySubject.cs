using Pragmatic.Privacy;

namespace Pragmatic.Identity.Auditing;

/// <summary>
///     The two steps between an identity a security event named and the pseudonym an entry carries.
/// </summary>
/// <remarks>
///     Written once and used by both handlers: the order matters and getting it wrong is silent. The
///     application says <em>which</em> subject through <see cref="ISecuritySubjectLocator" />, and the
///     framework turns that key into a reference through
///     <see cref="ObservedIdentityResolver" /> — which looks up and never allocates. Splitting the two
///     is what keeps an application from pseudonymising an address a stranger typed.
/// </remarks>
internal static class SecuritySubject
{
    public static async ValueTask<string?> ResolveAsync(
        ISecuritySubjectLocator subjects,
        ObservedIdentityResolver identities,
        string identity,
        LoginIdentityKind kind,
        CancellationToken ct)
    {
        if (await subjects.LocateAsync(identity, kind, ct).ConfigureAwait(false) is not { } key)
            return null;

        return await identities.ResolveAsync(key.SubjectType, key.Identifier, ct).ConfigureAwait(false);
    }
}
