using Microsoft.AspNetCore.Authorization;
using Pragmatic.Authorization;

namespace Pragmatic.Endpoints.Authorization;

/// <summary>
///     Authorization requirement that delegates permission checks to <see cref="IPermissionChecker" />.
///     Used by the source generator to bridge [RequirePermission] / [RequireAnyPermission]
///     with ASP.NET Core's authorization system.
/// </summary>
/// <remarks>
///     ⚠️ It lives here, and not with the handler that evaluates it, because <b>what an endpoint
///     requires has to be readable from the endpoint</b>. In <c>Pragmatic.Identity.AspNetCore</c>,
///     which a boundary library need not reference, a module without that package would get a policy
///     built from an opaque <c>RequireAssertion</c> instead: the request would still be refused, but
///     nothing could inspect what it wanted — and the 403 would come back without its
///     <c>requiredPermissions</c>, because the result handler reads them off this type.
///     Every boundary that declares an <c>[Endpoint]</c> already references this package, so putting
///     the requirement here is what makes the generated policy one shape.
///     <para>
///         The <b>handler</b> stays in <c>Pragmatic.Identity.AspNetCore</c>: evaluating the
///         requirement needs <see cref="IPermissionChecker" /> resolved from the container, which is
///         the host's business, not the module's.
///     </para>
/// </remarks>
public sealed class PragmaticPermissionRequirement : IAuthorizationRequirement
{
    public PragmaticPermissionRequirement(string[] permissions, PermissionMode mode = PermissionMode.All)
    {
        // A requirement with no permissions would silently grant (HasAll over an empty set is true),
        // so reject it as a misconfiguration rather than fail open.
        ArgumentNullException.ThrowIfNull(permissions);
        if (permissions.Length == 0)
            throw new ArgumentException("A permission requirement must list at least one permission.", nameof(permissions));
        Permissions = permissions;
        Mode = mode;
    }

    /// <summary>The permissions to check.</summary>
    public string[] Permissions { get; }

    /// <summary>Whether all or any permission is required.</summary>
    public PermissionMode Mode { get; }
}
