using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Pragmatic.MultiTenancy;

namespace Pragmatic.Persistence.EFCore.Repository;

/// <summary>
///     Which tenants a lookup cache has to be preloaded for.
/// </summary>
/// <remarks>
///     <para>
///         A lookup that is not tenant-scoped is loaded once, under no tenant — one row set for the
///         whole process, which is what a lookup table normally is.
///     </para>
///     <para>
///         ⚠️ A tenant-scoped one cannot be. Loaded exactly like the other — at startup, with no
///         tenant, against a <c>Set&lt;T&gt;()</c> whose filter fails closed — it would load
///         <b>zero</b> rows, and zero rows looks exactly like a lookup table nobody has filled yet.
///     </para>
///     <para>
///         So the tenants come from <see cref="ITenantStore" />, and the preload runs once per tenant.
///         ⚠️ "A store is registered" is not "the store knows the tenants": the default
///         <c>InMemoryTenantStore</c> starts empty unless the application seeds it, so a preload that
///         loops over it would run zero times — the same empty cache by another route. That case is said out
///         loud rather than left to look like success.
///     </para>
/// </remarks>
public static partial class LookupPreload
{
    /// <summary>
    ///     The tenants to load <paramref name="lookupName" /> for: a single <c>null</c> when it is not
    ///     tenant-scoped, one entry per active tenant when it is, and none when the store knows none.
    /// </summary>
    public static async Task<IReadOnlyList<string?>> TenantsToLoadAsync(
        IServiceProvider serviceProvider,
        bool tenantScoped,
        string lookupName,
        CancellationToken ct = default)
    {
        if (!tenantScoped)
            return [null];

        var logger = (ILogger?)serviceProvider.GetService<ILogger<LookupPreloadHostedService>>()
                     ?? NullLogger.Instance;

        var store = serviceProvider.GetService<ITenantStore>();
        if (store is null)
        {
            // Loud, because the alternative is loading nothing and calling it a preload. A
            // tenant-scoped lookup without a tenant store cannot be loaded at all, and the
            // application would run with every navigation over it throwing at request time.
            throw new InvalidOperationException(
                $"The lookup '{lookupName}' is tenant-scoped, so it must be preloaded once per tenant, " +
                "but no ITenantStore is registered. Register one — the generated host registers " +
                "InMemoryTenantStore by default — or drop the tenant scope from the entity.");
        }

        var tenants = await store.GetActiveAsync(ct).ConfigureAwait(false);

        if (tenants.Count == 0)
        {
            LogNoTenants(logger, lookupName, store.GetType().Name);
            return [];
        }

        return [.. tenants.Select(t => (string?)t.TenantId)];
    }

    [LoggerMessage(
        EventId = 6101,
        Level = LogLevel.Warning,
        Message = "Lookup '{Lookup}' is tenant-scoped and was preloaded for no tenant at all: " +
                  "{Store} reports no active tenants. This is not the same as an empty lookup table — " +
                  "every navigation over '{Lookup}' will fail until the store knows a tenant and the " +
                  "cache is loaded for it.")]
    private static partial void LogNoTenants(ILogger logger, string lookup, string store);
}
