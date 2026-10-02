using Pragmatic.Identity;

namespace Pragmatic.Authorization;

/// <summary>
///     Resolves permissions for the current user from an external source
///     (JWT claims, database, policy server, etc.).
///     Multiple providers are composed in order via <see cref="Order"/>.
/// </summary>
/// <remarks>
///     <para>
///         ⚠️ <b>Reading this application's own database needs care.</b> An ordinary repository read
///         evaluates the query filters, a permission-based filter asks the caller's permissions, and
///         resolution starts over — the resolver only caches its answer once every provider has
///         finished, so the cycle would be a stack overflow with no exception and no log. The
///         resolver breaks it: the reentrant ask returns the empty set and increments
///         <c>pragmatic.authorization.reentrant_resolutions</c>. A provider that needs its own rows
///         must read them past the filter pipeline — <c>IQueryFilterToggle.DisableAll()</c> — and
///         apply the tenant predicate itself, since disabling the filters disables that one too.
///     </para>
///     <para>
///         The tenant is available: authorization runs after tenant resolution, so a provider may
///         inject <c>ITenantContext</c> and scope its read to the workspace being served.
///         <see cref="ICurrentUser" />.<c>TenantId</c> is a different thing — the <c>tenant_id</c>
///         claim — and is null in an application that resolves tenants by header or subdomain
///         without issuing one.
///     </para>
/// </remarks>
public interface IPermissionProvider
{
    /// <summary>
    ///     Execution order. Lower values run first.
    ///     Convention: 0 = claims, 100 = role expansion, 200 = external.
    /// </summary>
    int Order { get; }

    /// <summary>
    ///     Resolves the set of permissions for the given user.
    ///     Results from all providers are merged (union).
    /// </summary>
    ValueTask<IReadOnlySet<string>> ResolvePermissionsAsync(
        ICurrentUser user, CancellationToken ct = default);
}
