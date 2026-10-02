namespace Pragmatic.MultiTenancy;

/// <summary>
///     Persistent store for tenant metadata.
///     Default: <c>InMemoryTenantStore</c> (seeded from configuration). No database-backed store ships:
///     an application whose tenants live in its database implements this over them — the Casework
///     example does, in <c>TheOrganisationsAreTheTenants</c>.
/// </summary>
[global::Pragmatic.Composition.Attributes.ProvidedByHost(global::Pragmatic.Composition.Attributes.Lifetime.Singleton)]
public interface ITenantStore
{
    /// <summary>
    ///     Gets a tenant by its unique identifier.
    /// </summary>
    Task<TenantInfo?> GetByIdAsync(string tenantId, CancellationToken ct = default);

    /// <summary>
    ///     Returns all registered tenants (including suspended/deactivated).
    /// </summary>
    Task<IReadOnlyList<TenantInfo>> GetAllAsync(CancellationToken ct = default);

    /// <summary>
    ///     Returns only active tenants (state = <see cref="TenantState.Active"/>).
    /// </summary>
    Task<IReadOnlyList<TenantInfo>> GetActiveAsync(CancellationToken ct = default);

    /// <summary>
    ///     Registers a new tenant. Returns the created <see cref="TenantInfo"/>.
    ///     <para>
    ///         If a tenant with the same <c>TenantId</c> already exists, implementations
    ///         must throw an <see cref="InvalidOperationException"/> rather than silently
    ///         updating or ignoring the duplicate, to preserve data integrity.
    ///     </para>
    /// </summary>
    Task<TenantInfo> CreateAsync(TenantInfo tenant, CancellationToken ct = default);

    /// <summary>
    ///     Updates an existing tenant's metadata and/or state.
    ///     Returns true if the tenant was found and updated.
    /// </summary>
    Task<bool> UpdateAsync(TenantInfo tenant, CancellationToken ct = default);

    /// <summary>
    ///     Transitions a tenant to <see cref="TenantState.Deactivated"/>.
    ///     Returns true if the tenant was found and deactivated.
    /// </summary>
    Task<bool> DeactivateAsync(string tenantId, CancellationToken ct = default);

    /// <summary>
    ///     Permanently removes a tenant's metadata record from the store. Returns true if the
    ///     tenant was found and removed, false if no such tenant existed.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         <b>Destructive and irreversible.</b> Unlike <see cref="DeactivateAsync"/> (a
    ///         reversible soft transition to <see cref="TenantState.Deactivated"/>), this hard
    ///         delete erases the record entirely — there is no undo. It does <b>not</b> drop the
    ///         tenant's data/database; it only removes the tenant entry from this store. Callers
    ///         should almost always prefer <see cref="DeactivateAsync"/>; reserve
    ///         <see cref="DeleteAsync"/> for compliance erasure or test cleanup.
    ///     </para>
    ///     <para>
    ///         The default implementation throws <see cref="NotSupportedException"/> so that
    ///         append-only or audited stores that intentionally forbid hard deletion stay
    ///         source-compatible without silently no-op'ing. Stores that support removal must
    ///         override this method.
    ///     </para>
    /// </remarks>
    /// <exception cref="NotSupportedException">
    ///     Thrown when the store does not support hard deletion.
    /// </exception>
    Task<bool> DeleteAsync(string tenantId, CancellationToken ct = default)
        => throw new NotSupportedException(
            $"{GetType().Name} does not support hard delete. " +
            "Use DeactivateAsync for a reversible soft transition, or override ITenantStore.DeleteAsync.");
}
