namespace Pragmatic.MultiTenancy;

/// <summary>
///     Notified when a tenant is created, updated, deactivated or deleted.
/// </summary>
/// <remarks>
///     <para>
///         Implement this to keep anything derived from the <b>set of tenants</b> in step with it.
///         The four write methods of <see cref="ITenantStore" /> tell nobody by themselves, so without
///         this a per-tenant cache built at startup goes stale the moment a tenant is added, with no
///         symptom until something reads it.
///     </para>
///     <para>
///         Register implementations in DI; <see cref="ObservedTenantStore" /> collects them and raises
///         the calls. Every registered observer is told about every transition — this is a broadcast,
///         not a chain, and no observer can veto a write that has already happened.
///     </para>
///     <para>
///         An observer that throws is logged and skipped: the tenant was created (or removed) before
///         anybody was told, and failing the caller would report a write that happened as one that did
///         not. It is therefore the observer's job to make its own failure visible.
///     </para>
///     <para>
///         The calls are awaited before the store's method returns, so a caller that creates a tenant
///         and then serves a request for it does not race the refresh.
///     </para>
/// </remarks>
public interface ITenantLifecycleObserver
{
    /// <summary>A tenant has been registered. Its state may be anything, including deactivated.</summary>
    Task OnCreatedAsync(TenantInfo tenant, CancellationToken ct = default);

    /// <summary>
    ///     A tenant's metadata or state has changed. The new state travels on <paramref name="tenant" />
    ///     — this is how a deactivated tenant comes back, so an observer that assumes "updated means
    ///     still active" will miss both directions.
    /// </summary>
    Task OnUpdatedAsync(TenantInfo tenant, CancellationToken ct = default);

    /// <summary>A tenant has been moved to <see cref="TenantState.Deactivated" />. Reversible.</summary>
    Task OnDeactivatedAsync(string tenantId, CancellationToken ct = default);

    /// <summary>A tenant's record has been removed from the store. Not reversible.</summary>
    Task OnDeletedAsync(string tenantId, CancellationToken ct = default);
}
