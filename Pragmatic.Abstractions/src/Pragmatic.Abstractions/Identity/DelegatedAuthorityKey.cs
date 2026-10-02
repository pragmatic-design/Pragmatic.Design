using System.Security.Cryptography;
using System.Text;

namespace Pragmatic.Identity;

/// <summary>
///     The segment a cache key carries for a delegated caller, so that an answer computed under one
///     delegated authority is never served to another.
/// </summary>
/// <remarks>
///     <para>
///         The authority of a delegated session is not a function of the subject: it is composed from the
///         subject, the actor, the policy and — under <see cref="DelegationPolicy.GrantScoped" /> — the
///         grant, and a delegation can be <b>refused</b> (expired, a chain too deep, an actor of another
///         tenant) without any of those identifiers changing, in which case it holds no permission at all.
///         A key that names the identifiers alone lets a refused delegation read what the admitted one was
///         answered, and lets one grant read what another was answered.
///     </para>
///     <para>
///         So the segment names the identifiers <b>and</b> an imprint of the effective permission set —
///         the set every permission check of the request is answered from, after admission. Two callers
///         share an entry only when both agree.
///     </para>
///     <para>
///         The imprint is SHA-256 rather than <see cref="string.GetHashCode()" />, which is randomised per
///         process: a distributed cache is shared between hosts, and each would compute a different key
///         for the same authority.
///     </para>
///     <para>
///         While the permissions are being resolved the set is a guard's empty one, which is not the
///         caller's authority; the segment says so instead of imprinting it, so an entry written then is
///         not mistaken for one written under an authority that really is empty.
///     </para>
/// </remarks>
public static class DelegatedAuthorityKey
{
    /// <summary>
    ///     The segment for <paramref name="user" />, ending in <c>:</c>, or <see langword="null" /> when the
    ///     session is not delegated — so an application that never delegates keeps its keys unchanged.
    /// </summary>
    /// <param name="user">The caller.</param>
    public static string? For(ICurrentUser user)
    {
        ArgumentNullException.ThrowIfNull(user);

        if (user.Delegation is not { } delegation)
            return null;

        var authority = user.Authorization.IsResolvingPermissions
            ? "resolving"
            : Imprint(user.Authorization.Permissions);

        return $"d:{delegation.ActorId}:{delegation.Policy}:{delegation.GrantId ?? "-"}:{authority}:";
    }

    private static string Imprint(IReadOnlySet<string> permissions)
    {
        var sorted = permissions.Order(StringComparer.Ordinal);
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(string.Join('\n', sorted)));
        return Convert.ToHexString(hash, 0, 8);
    }
}
