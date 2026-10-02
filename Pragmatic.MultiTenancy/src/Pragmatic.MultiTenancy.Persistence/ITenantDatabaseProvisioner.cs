namespace Pragmatic.MultiTenancy.Persistence;

/// <summary>
///     Creates databases for new tenants.
///     Default: <see cref="NoOpTenantProvisioner"/> (external actor manages provisioning).
///     Pluggable: <see cref="PostgresTenantProvisioner"/>, <see cref="SqlServerTenantProvisioner"/>.
/// </summary>
public interface ITenantDatabaseProvisioner
{
    /// <summary>
    ///     Creates a database for the specified tenant if it doesn't already exist.
    /// </summary>
    /// <param name="tenantId">The tenant identifier.</param>
    /// <param name="connectionString">The connection string for the tenant's database.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>True if the database was created, false if it already existed.</returns>
    Task<bool> ProvisionAsync(string tenantId, string connectionString, CancellationToken ct = default);

    /// <summary>
    ///     Checks whether the database for a tenant already exists.
    /// </summary>
    Task<bool> ExistsAsync(string connectionString, CancellationToken ct = default);
}
