using System.Data.Common;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Pragmatic.Migrations.Tenant;

namespace Pragmatic.MultiTenancy.Persistence;

/// <summary>
///     EF Core connection interceptor that routes each DbContext to the current tenant's dedicated
///     database (MT-H2). On connection open it rewrites the connection string to the tenant's dedicated
///     one when the tenant has it; tenants without a dedicated database keep the shared connection
///     (row-level isolation). The async path (<see cref="ConnectionOpeningAsync"/>) resolves without
///     sync-over-async; the sync path prefers the warm cache and blocks only on a cold miss.
/// </summary>
/// <remarks>
///     Registered (scoped) by <c>AddDbPerTenant</c> and auto-discovered by EF Core from the application
///     service provider, so it applies to every DbContext without changing their registration.
/// </remarks>
public sealed class TenantConnectionInterceptor(
    ITenantContext tenantContext,
    TenantConnectionResolver resolver) : DbConnectionInterceptor
{
    public override InterceptionResult ConnectionOpening(
        DbConnection connection, ConnectionEventData eventData, InterceptionResult result)
    {
        if (tenantContext is { IsResolved: true, TenantId: { } tenantId })
        {
            var cs = resolver.ResolveCached(tenantId)
                     ?? resolver.ResolveAsync(tenantId).AsTask().GetAwaiter().GetResult();
            if (!string.IsNullOrEmpty(cs))
                connection.ConnectionString = cs;
        }

        return result;
    }

    public override async ValueTask<InterceptionResult> ConnectionOpeningAsync(
        DbConnection connection, ConnectionEventData eventData, InterceptionResult result,
        CancellationToken cancellationToken = default)
    {
        if (tenantContext is { IsResolved: true, TenantId: { } tenantId })
        {
            var cs = await resolver.ResolveAsync(tenantId, cancellationToken).ConfigureAwait(false);
            if (!string.IsNullOrEmpty(cs))
                connection.ConnectionString = cs;
        }

        return result;
    }

    /// <summary>
    ///     A connection that did not open, named by the tenant it belonged to.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         Measured before it was written: an exception thrown from
    ///         <c>ConnectionFailed(Async)</c> reaches the caller and <b>replaces</b> the original, which
    ///         is why the original is carried as the inner exception rather than dropped.
    ///     </para>
    ///     <para>
    ///         ⚠️ One guard, and it is the point rather than caution: a tenant on the <b>shared</b>
    ///         database had nothing rewritten for it, so its failure is not a tenant database's and
    ///         must keep saying what it always said.
    ///     </para>
    ///     <para>
    ///         ⚠️ There is deliberately <b>no</b> guard for a probe. <c>ConnectionErrorEventData</c>
    ///         carries no flag that tells one apart — EF Core opens a probe with errors expected, but
    ///         that only changes the log level and the hook runs either way. So it was measured instead:
    ///         <c>CanConnectAsync</c> swallows what this throws and still answers, which is pinned by
    ///         <c>AskingWhetherTheDatabaseIsThere_StillAnswersInsteadOfThrowing</c>.
    ///     </para>
    ///     <para>
    ///         No round trip is added: nothing checks whether the database exists. This runs only on a
    ///         connection that has already failed.
    ///     </para>
    /// </remarks>
    public override void ConnectionFailed(DbConnection connection, ConnectionErrorEventData eventData)
    {
        if (DedicatedDatabase() is { } database)
            throw new TenantDatabaseUnavailableException(tenantContext.TenantId!, database, eventData.Exception);
    }

    /// <inheritdoc cref="ConnectionFailed" />
    public override Task ConnectionFailedAsync(
        DbConnection connection, ConnectionErrorEventData eventData, CancellationToken cancellationToken = default)
    {
        if (DedicatedDatabase() is { } database)
            throw new TenantDatabaseUnavailableException(tenantContext.TenantId!, database, eventData.Exception);

        return Task.CompletedTask;
    }

    /// <summary>
    ///     The database the current tenant has of its own, or <c>null</c> when it has none and the
    ///     failure therefore belongs to the shared database.
    /// </summary>
    /// <remarks>
    ///     The resolver is asked from its <b>cache</b>, which the open that just failed warmed a moment
    ///     ago: a failure path is the last place to start resolving over the network. A cache miss
    ///     answers <c>null</c>, so the provider's own error propagates — silence rather than a guess.
    /// </remarks>
    private string? DedicatedDatabase()
    {
        if (tenantContext is not { IsResolved: true, TenantId: { } tenantId })
            return null;

        var dedicated = resolver.ResolveCached(tenantId);

        return string.IsNullOrEmpty(dedicated) ? null : ConnectionStringInfo.DatabaseIn(dedicated);
    }
}
