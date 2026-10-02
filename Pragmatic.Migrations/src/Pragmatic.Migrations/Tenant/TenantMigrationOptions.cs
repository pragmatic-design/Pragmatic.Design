namespace Pragmatic.Migrations.Tenant;

/// <summary>
///     Options for tenant migration orchestration.
/// </summary>
public sealed class TenantMigrationOptions
{
    /// <summary>
    ///     Maximum number of tenants to migrate at the same time.
    ///     Default: 1 (sequential — safest for DB server load, and the only mode in which
    ///     <see cref="ContinueOnFailure" /> can stop the sweep).
    /// </summary>
    /// <remarks>Values below 1 are treated as 1.</remarks>
    public int MaxParallelism
    {
        get;
        set => field = value < 1 ? 1 : value;
    } = 1;

    /// <summary>
    ///     Whether to keep migrating the remaining tenants after one fails.
    ///     Default: false — stop at the first failure, leaving the untouched tenants counted as
    ///     skipped in the <see cref="TenantMigrationSummary" />.
    /// </summary>
    /// <remarks>
    ///     Only meaningful when <see cref="MaxParallelism" /> is 1: with several migrations already
    ///     in flight there is no coherent point at which to stop, so a parallel sweep always
    ///     attempts every tenant.
    /// </remarks>
    public bool ContinueOnFailure { get; set; }

    /// <summary>
    ///     Whether a tenant whose migration failed is moved to <c>Suspended</c>.
    ///     Default: true. Set to false to leave it <c>Migrating</c>, so a retry can pick it up
    ///     without an operator un-suspending it first.
    /// </summary>
    public bool SuspendOnFailure { get; set; } = true;

    /// <summary>
    ///     Maximum time a single tenant migration may take before it is abandoned and reported as
    ///     that tenant's failure. Keeps one unreachable database from stalling the whole sweep.
    ///     Default: 10 minutes.
    /// </summary>
    public TimeSpan TenantTimeout { get; set; } = TimeSpan.FromMinutes(10);
}
