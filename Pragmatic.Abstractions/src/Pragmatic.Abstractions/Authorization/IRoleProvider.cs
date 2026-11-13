using Pragmatic.Identity;

namespace Pragmatic.Authorization;

/// <summary>
///     Resolves roles the caller holds beyond those their identity carries: roles assigned in a store, such as
///     Pragmatic.Authorization.Management's. The results of every provider are joined to the identity's <c>role</c> claims in
///     <see cref="IUserAuthorization.Roles" />.
/// </summary>
/// <remarks>
///     <para>
///         The twin of <see cref="IPermissionProvider" />, for the names rather than what they grant. A role a store assigns already
///         grants its permissions through a permission provider; without this its name was missing from the caller's roles, so a role
///         check (<c>IsInRole</c>) refused what the permissions allowed (#723).
///     </para>
///     <para>
///         The same care applies as to a permission provider: a provider that reads this application's own database through a filtered
///         repository asks for the caller's permissions, which ask for the roles; the resolver answers that reentrant ask with the
///         identity's roles alone.
///     </para>
/// </remarks>
public interface IRoleProvider
{
    /// <summary>The roles <paramref name="user" /> holds through this provider; empty when none.</summary>
    ValueTask<IReadOnlyCollection<string>> ResolveRolesAsync(ICurrentUser user, CancellationToken ct = default);
}
