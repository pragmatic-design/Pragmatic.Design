using System.Collections.Immutable;
using Pragmatic.SourceGen;

namespace Pragmatic.SourceGenerator.Features.Endpoints.Models;

/// <summary>
///     Model representing authorization configuration.
/// </summary>
internal sealed record AuthorizationModel
{
    /// <summary>
    ///     Whether authorization is required.
    /// </summary>
    public bool IsRequired { get; init; }

    /// <summary>
    ///     Whether anonymous access is allowed.
    /// </summary>
    public bool AllowAnonymous { get; init; }

    /// <summary>
    ///     Whether the route belongs to no tenant, so tenant resolution does not require one.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         Not authorization, and it rides here for a reason: this is the one model that already
    ///         reaches every route-configuration renderer — the endpoint, mutation, query and
    ///         domain-action handlers all read it to emit <c>AllowAnonymous()</c>. A second channel to
    ///         the same four places would be four more copies of the same plumbing.
    ///     </para>
    ///     <para>
    ///         ⚠️ Before this existed, <c>TenantAgnosticEndpoint</c> was attached from exactly one
    ///         place inside the framework and no attribute declared it, so a module could not ask for
    ///         it: an anonymous liveness probe was refused with 400 in a multi-tenant host, before the
    ///         route ran.
    ///     </para>
    /// </remarks>
    public bool RouteIsTenantAgnostic { get; init; }

    /// <summary>
    ///     The authorization policy name.
    /// </summary>
    public string? PolicyName { get; init; }

    /// <summary>
    ///     Required permissions (all must be present).
    /// </summary>
    public EquatableArray<string> RequiredPermissions { get; init; } = EquatableArray<string>.Empty;

    /// <summary>
    ///     Any of these permissions grants access.
    /// </summary>
    public EquatableArray<string> AnyPermissions { get; init; } = EquatableArray<string>.Empty;

    /// <summary>
    ///     Required roles (all must be present).
    /// </summary>
    public EquatableArray<string> RequiredRoles { get; init; } = EquatableArray<string>.Empty;

    /// <summary>
    ///     Constant paths written in <c>[RequirePermission]</c> that could not be bound — e.g.
    ///     <c>BillingPermissions.Invoice.Read</c>, a constant this generator itself emits. Kept as
    ///     written in source and resolved against the permission catalog after every producer has
    ///     contributed to it; the permission would otherwise be dropped and never enforced.
    /// </summary>
    public EquatableArray<string> UnresolvedRequiredPermissionPaths { get; init; } = EquatableArray<string>.Empty;

    /// <summary>
    ///     The same, for <c>[RequireAnyPermission]</c>.
    /// </summary>
    public EquatableArray<string> UnresolvedAnyPermissionPaths { get; init; } = EquatableArray<string>.Empty;

    /// <summary>
    ///     Whether any constant path still awaits resolution against the permission catalog.
    /// </summary>
    public bool HasUnresolvedPermissionPaths
        => !UnresolvedRequiredPermissionPaths.IsDefaultOrEmpty || !UnresolvedAnyPermissionPaths.IsDefaultOrEmpty;
}
