using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Pragmatic.MultiTenancy;
using Pragmatic.Persistence.Entity;

namespace Pragmatic.Persistence.EFCore.Repository;

/// <summary>
///     Keeps the lookup caches in step with the set of tenants.
/// </summary>
/// <remarks>
///     <para>
///         The startup preload loads a tenant-scoped <c>[Lookup]</c> once per active tenant and stops
///         there. Without this observer a tenant created afterwards would have no cache, and every
///         navigation over that lookup would throw for it until the host restarted; a tenant that went
///         away would keep its rows in memory for whoever gets that tenant id next. This is the consumer that makes the tenant lifecycle signal
///         do something.
///     </para>
///     <para>
///         A lookup that is not tenant-scoped is untouched in both directions: its loader does nothing
///         per tenant, and <see cref="LookupResolver.ForgetTenant" /> leaves the shared caches alone.
///     </para>
/// </remarks>
public sealed partial class LookupCacheTenantObserver(
    IServiceProvider serviceProvider,
    IEnumerable<ILookupCacheLoader> loaders,
    ILogger<LookupCacheTenantObserver>? logger = null) : ITenantLifecycleObserver
{
    private readonly ILogger _logger = logger ?? NullLogger<LookupCacheTenantObserver>.Instance;

    /// <inheritdoc />
    public Task OnCreatedAsync(TenantInfo tenant, CancellationToken ct = default)
        => FollowStateAsync(tenant, ct);

    /// <inheritdoc />
    public Task OnUpdatedAsync(TenantInfo tenant, CancellationToken ct = default)
        => FollowStateAsync(tenant, ct);

    /// <inheritdoc />
    public Task OnDeactivatedAsync(string tenantId, CancellationToken ct = default)
    {
        LookupResolver.ForgetTenant(tenantId);
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task OnDeletedAsync(string tenantId, CancellationToken ct = default)
    {
        LookupResolver.ForgetTenant(tenantId);
        return Task.CompletedTask;
    }

    /// <summary>
    ///     Loads for an active tenant, forgets one that is not.
    /// </summary>
    /// <remarks>
    ///     The state travels on the tenant for both create and update, and it is read rather than
    ///     assumed: the startup preload loads the <b>active</b> tenants, so loading for every tenant
    ///     that is merely mentioned would make the two disagree, and an update is also how a tenant is
    ///     suspended without <c>DeactivateAsync</c> ever being called.
    /// </remarks>
    private async Task FollowStateAsync(TenantInfo tenant, CancellationToken ct)
    {
        if (tenant.State != TenantState.Active)
        {
            LookupResolver.ForgetTenant(tenant.TenantId);
            return;
        }

        foreach (var loader in loaders)
        {
            try
            {
                await loader.LoadForTenantAsync(serviceProvider, tenant.TenantId, ct).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // Unlike the startup preload, which refuses to start rather than run with an
                // incomplete cache: here the tenant already exists, and throwing back at the caller
                // would report a create that happened as one that did not. One loader failing must
                // also not cost the others their load.
                LogLoaderFailed(_logger, ex, loader.GetType().Name, tenant.TenantId);
            }
        }
    }

    [LoggerMessage(
        EventId = 6102,
        Level = LogLevel.Error,
        Message = "Lookup cache loader {Loader} failed to load tenant '{TenantId}', which was created " +
                  "or reactivated while the host was running. Every navigation over that lookup will " +
                  "fail for this tenant until the cache is loaded.")]
    private static partial void LogLoaderFailed(
        ILogger logger, Exception ex, string loader, string tenantId);
}
