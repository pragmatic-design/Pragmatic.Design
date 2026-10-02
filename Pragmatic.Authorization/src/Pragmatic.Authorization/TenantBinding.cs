using Pragmatic.Identity;

namespace Pragmatic.Authorization;

/// <summary>
///     Whether a caller may act on a tenant it names: the rule every management operation that takes a
///     tenant as input applies before touching data.
/// </summary>
/// <remarks>
///     <para>
///         Management permissions are flat — <c>configuration.values.write</c>,
///         <c>authorization.roles.manage</c> — and say nothing about which tenant. Without a binding, an
///         administrator of one tenant reaches another by naming it.
///     </para>
///     <para>
///         ⚠️ <b>Naming no tenant is not a way out.</b> A <see langword="null" /> target is the global
///         level — the base value every tenant inherits, a role that holds everywhere — so acting on it is
///         acting on every tenant at once. Only a caller that belongs to no tenant may do that. The first
///         version of this rule let a <see langword="null" /> target through for everyone.
///     </para>
/// </remarks>
public static class TenantBinding
{
    /// <summary>
    ///     <see langword="true" /> when <paramref name="caller" /> belongs to no tenant, or belongs to
    ///     exactly <paramref name="targetTenantId" />.
    /// </summary>
    /// <param name="caller">Who is asking.</param>
    /// <param name="targetTenantId">The tenant the operation acts on; <see langword="null" /> or empty for the global level.</param>
    public static bool Permits(ICurrentUser caller, string? targetTenantId)
    {
        ArgumentNullException.ThrowIfNull(caller);

        if (caller.TenantId is not { Length: > 0 } callerTenant)
            return true;

        return string.Equals(targetTenantId, callerTenant, StringComparison.Ordinal);
    }
}
