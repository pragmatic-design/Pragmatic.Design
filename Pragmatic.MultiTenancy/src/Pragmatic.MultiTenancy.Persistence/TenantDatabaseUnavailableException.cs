namespace Pragmatic.MultiTenancy.Persistence;

/// <summary>
///     Thrown when opening the connection to a tenant's <b>dedicated</b> database fails.
/// </summary>
/// <remarks>
///     <para>
///         The register said the tenant was there and named a database of its own; the connection to
///         that database did not open. The common cause is that nobody created it: provisioning is
///         explicit in this framework, so a row in the register is not a database.
///     </para>
///     <para>
///         ⚠️ The provider's own exception is the <see cref="System.Exception.InnerException" /> and is
///         not lost — it carries the SQL state and the driver's wording. What is added is the one fact
///         the driver cannot know and everything downstream has already lost: <b>which tenant</b> this
///         connection belonged to. <see cref="TenantConnectionInterceptor" /> is the only place that
///         still holds it, because it is what rewrote the connection string a moment earlier.
///     </para>
///     <para>
///         A tenant on the <b>shared</b> database never produces this: nothing was rewritten for it, so
///         a failure there is the application's own connection failing and keeps saying what it said.
///     </para>
/// </remarks>
public sealed class TenantDatabaseUnavailableException(string tenantId, string database, Exception inner)
    : InvalidOperationException(
        $"The connection to tenant '{tenantId}' dedicated database '{database}' could not be opened. "
        + "A tenant row is not a database: provisioning is explicit — call "
        + "ITenantDatabaseProvisioner.ProvisionAsync to create it, then IMigrationRunner.MigrateAsync "
        + "with this host's schema to bring it to the current version. "
        + "The provider's own error is the inner exception.",
        inner)
{
    /// <summary>The tenant whose connection failed.</summary>
    public string TenantId { get; } = tenantId;

    /// <summary>The database its connection string names.</summary>
    public string Database { get; } = database;
}
