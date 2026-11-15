using Microsoft.Extensions.Hosting;

namespace Pragmatic.MultiTenancy.Persistence;

/// <summary>
///     Refuses to start a host on a database per tenant whose <see cref="ITenantStore" /> is the one the generator wrote over a
///     <c>[TenantSource]</c> entity.
/// </summary>
/// <remarks>
///     That store reads the tenants through the entity's repository, whose context the database-per-tenant routing opens on the tenant's
///     own database, and the routing asks the store which database that is: the first request would recurse instead of failing. The
///     database-per-tenant choice is made at run time (<c>UseDbPerTenant</c>), so the generator cannot see it, and the refusal is here,
///     with a message, at startup. A host on a database per tenant registers a store of its own that opens the shared connection itself.
/// </remarks>
internal sealed class GeneratedTenantStoreRefusal(ITenantStore store) : IHostedService
{
    public Task StartAsync(CancellationToken cancellationToken)
    {
        var answering = store is ObservedTenantStore observed ? observed.Inner : store;
        if (answering is IEntityTenantStore generated)
        {
            throw new InvalidOperationException(
                $"The tenant store is the one generated over [TenantSource] on {generated.EntityType.Name}, and this host runs a database "
                + "per tenant (AddDbPerTenant). That store reads through the tenant-routed context, which asks the store which database to "
                + "open. Register an ITenantStore of your own that reads the tenants from the shared database, as the Casework example does.");
        }

        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
