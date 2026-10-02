namespace Pragmatic.MultiTenancy;

/// <summary>
///     An <see cref="ITenantContext" /> whose tenant can be assigned at runtime within the
///     current scope. Implemented by the scoped tenant context so that non-HTTP entry points
///     (outbox/event delivery, background jobs) can restore the originating tenant before
///     invoking a handler — the SAME context the EF tenant query filter reads.
/// </summary>
/// <remarks>
///     <para>
///         This is the <b>request</b> path: the HTTP tenant-resolution middleware sets the tenant on
///         the scoped context at the start of a request.
///     </para>
///     <para>
///         ⚠️ <b>Work that runs outside a request declares its tenant with
///         <see cref="TenantScope" />, not here.</b> <see cref="TenantScope" /> is the one mechanism
///         for background jobs, tests and seed, and it lives in Abstractions so that
///         workers, which reference only Abstractions, can call it.
///     </para>
/// </remarks>
public interface IMutableTenantContext : ITenantContext
{
    /// <summary>
    ///     Sets the current tenant for this scope and returns a handle that restores the
    ///     previous tenant on dispose. Use within a <c>using</c> / <c>try…finally</c>.
    /// </summary>
    /// <param name="tenantId">The tenant identifier to assume. Null clears the tenant.</param>
    /// <param name="tenantName">Optional human-readable tenant name.</param>
    /// <returns>A disposable that restores the previous tenant.</returns>
    IDisposable SetTenant(string? tenantId, string? tenantName = null);
}
