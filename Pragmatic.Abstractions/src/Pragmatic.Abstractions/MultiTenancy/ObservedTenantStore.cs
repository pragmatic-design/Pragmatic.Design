using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Pragmatic.MultiTenancy;

/// <summary>
///     An <see cref="ITenantStore" /> that tells its <see cref="ITenantLifecycleObserver" />s when a
///     tenant appears, changes or goes away.
/// </summary>
/// <remarks>
///     <para>
///         The signal lives in a decorator and not in the stores because there are three stores in the
///         framework and one more in every application that writes its own: "each store raises it" is
///         a rule that is true until somebody writes the fourth. Wrapping the registration instead
///         makes it true for stores that do not know this type exists.
///     </para>
///     <para>
///         ⚠️ It only sees writes that go <b>through</b> <see cref="ITenantStore" />. A database-backed
///         store whose table is also written by a migration, an admin script or another process will
///         not announce those, and a consumer that must survive them needs its own reconciliation.
///     </para>
///     <para>
///         The registration builds it from the container, because the observers come from there: the
///         generated host wraps the default store, and anything that replaces the <c>ITenantStore</c>
///         registration afterwards — the Agent client does — has to wrap its own.
///     </para>
/// </remarks>
public sealed partial class ObservedTenantStore(
    ITenantStore inner,
    IEnumerable<ITenantLifecycleObserver> observers,
    ILogger<ObservedTenantStore>? logger = null) : ITenantStore
{
    private readonly ITenantLifecycleObserver[] _observers = [.. observers];
    private readonly ILogger _logger = logger ?? NullLogger<ObservedTenantStore>.Instance;

    /// <inheritdoc />
    public Task<TenantInfo?> GetByIdAsync(string tenantId, CancellationToken ct = default)
        => inner.GetByIdAsync(tenantId, ct);

    /// <inheritdoc />
    public Task<IReadOnlyList<TenantInfo>> GetAllAsync(CancellationToken ct = default)
        => inner.GetAllAsync(ct);

    /// <inheritdoc />
    public Task<IReadOnlyList<TenantInfo>> GetActiveAsync(CancellationToken ct = default)
        => inner.GetActiveAsync(ct);

    /// <inheritdoc />
    public async Task<TenantInfo> CreateAsync(TenantInfo tenant, CancellationToken ct = default)
    {
        var created = await inner.CreateAsync(tenant, ct).ConfigureAwait(false);
        await NotifyAsync(o => o.OnCreatedAsync(created, ct), "created", created.TenantId)
            .ConfigureAwait(false);
        return created;
    }

    /// <inheritdoc />
    public async Task<bool> UpdateAsync(TenantInfo tenant, CancellationToken ct = default)
        => await RaiseIfChangedAsync(
            inner.UpdateAsync(tenant, ct),
            o => o.OnUpdatedAsync(tenant, ct),
            "updated",
            tenant.TenantId).ConfigureAwait(false);

    /// <inheritdoc />
    public async Task<bool> DeactivateAsync(string tenantId, CancellationToken ct = default)
        => await RaiseIfChangedAsync(
            inner.DeactivateAsync(tenantId, ct),
            o => o.OnDeactivatedAsync(tenantId, ct),
            "deactivated",
            tenantId).ConfigureAwait(false);

    /// <inheritdoc />
    public async Task<bool> DeleteAsync(string tenantId, CancellationToken ct = default)
        => await RaiseIfChangedAsync(
            inner.DeleteAsync(tenantId, ct),
            o => o.OnDeletedAsync(tenantId, ct),
            "deleted",
            tenantId).ConfigureAwait(false);

    /// <summary>
    ///     Announces the transition only when the store says it happened.
    /// </summary>
    /// <remarks>
    ///     The three write methods return false when there was no such tenant. Announcing a transition
    ///     that did not happen is worse than announcing none: a consumer would drop what it derived for
    ///     a tenant that is still active.
    /// </remarks>
    private async Task<bool> RaiseIfChangedAsync(
        Task<bool> write,
        Func<ITenantLifecycleObserver, Task> raise,
        string transition,
        string tenantId)
    {
        var changed = await write.ConfigureAwait(false);
        if (changed)
            await NotifyAsync(raise, transition, tenantId).ConfigureAwait(false);

        return changed;
    }

    private async Task NotifyAsync(
        Func<ITenantLifecycleObserver, Task> raise,
        string transition,
        string tenantId)
    {
        foreach (var observer in _observers)
        {
            try
            {
                await raise(observer).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                LogObserverFailed(_logger, ex, observer.GetType().Name, transition, tenantId);
            }
        }
    }

    [LoggerMessage(
        EventId = 1181,
        Level = LogLevel.Error,
        Message = "Tenant lifecycle observer {Observer} threw on '{Transition}' for tenant " +
                  "'{TenantId}'. The transition itself succeeded and is not rolled back, so whatever " +
                  "this observer derives from the set of tenants is now stale for that tenant.")]
    private static partial void LogObserverFailed(
        ILogger logger, Exception ex, string observer, string transition, string tenantId);
}
