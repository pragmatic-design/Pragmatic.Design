namespace Pragmatic.MultiTenancy.Persistence;

/// <summary>
///     Default provisioner that does nothing — assumes an external actor
///     (CLI, Azure Function, CI/CD pipeline) manages database creation via the control plane.
/// </summary>
public sealed class NoOpTenantProvisioner : ITenantDatabaseProvisioner
{
    /// <summary>
    ///     Returns <c>false</c> unconditionally: this provisioner is a no-op and does
    ///     not create databases. Callers that need actual provisioning must replace this
    ///     with <c>PostgresTenantProvisioner</c> or <c>SqlServerTenantProvisioner</c> via
    ///     <c>UseAutoProvision&lt;T&gt;()</c>.
    /// </summary>
    public Task<bool> ProvisionAsync(string tenantId, string connectionString, CancellationToken ct = default)
        => Task.FromResult(false);

    /// <summary>
    ///     Always returns <c>true</c> — assumes the database exists because an external
    ///     actor (CLI, Azure Function, CI/CD pipeline) is responsible for provisioning.
    ///     This is an INTENTIONAL no-op; callers wiring tenant resolution that needs a
    ///     real existence check must replace this with a concrete provisioner.
    /// </summary>
    public Task<bool> ExistsAsync(string connectionString, CancellationToken ct = default)
        => Task.FromResult(true); // Assume it exists — external provisioning is responsible
}
